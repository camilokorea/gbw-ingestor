using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

/// <summary>
/// Processes the iNat portion of IngestionQueue (Source = "inat").
/// Mirrors IngestionService exactly in queue-batch-lock-process-save pattern.
/// </summary>
public class INatIngestionService : IINatIngestionService
{
    private readonly IINatApiClient _apiClient;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<INatIngestionService> _logger;
    private readonly IConfiguration _configuration;

    public INatIngestionService(
        IINatApiClient apiClient,
        IApplicationDbContext dbContext,
        ILogger<INatIngestionService> logger,
        IConfiguration configuration)
    {
        _apiClient = apiClient;
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task ProcessQueueAsync()
    {
        const int batchSize = 10;
        var daysAgo = _configuration.GetValue<int>("iNatIngestor:DaysAgo", 180);
        var maxPages = _configuration.GetValue<int>("iNatIngestor:MaxPagesPerCountry", 5);

        _logger.LogInformation("Starting iNat ingestion. Batch size: {BatchSize}.", batchSize);

        var queueItems = await _dbContext.IngestionQueue
            .Where(i => i.Source == "inat" &&
                       (i.Status == IngestionStatus.Queued || i.Status == IngestionStatus.Failed) &&
                       i.AttemptCount < 10)
            .OrderBy(i => i.LastAttemptUtc)
            .Take(batchSize)
            .ToListAsync();

        if (!queueItems.Any())
        {
            _logger.LogInformation("No iNat regions in the queue to process.");
            return;
        }

        // Lock items
        foreach (var item in queueItems)
        {
            item.Status = IngestionStatus.Processing;
            item.LastAttemptUtc = DateTime.UtcNow;
            item.AttemptCount++;
        }
        await _dbContext.SaveChangesAsync();

        foreach (var item in queueItems)
        {
            _logger.LogInformation("Processing iNat region: {RegionCode}", item.RegionCode);

            var placeId = INatCountryPlaceIds.GetPlaceId(item.RegionCode);
            if (placeId == null)
            {
                _logger.LogWarning("No iNat place ID for country {RegionCode}. Skipping.", item.RegionCode);
                item.Status = IngestionStatus.Completed;
                await _dbContext.SaveChangesAsync();
                continue;
            }

            try
            {
                var country = await GetOrCreateCountryAsync(item.RegionCode);
                var totalIngested = 0;

                for (var page = 1; page <= maxPages; page++)
                {
                    var observations = (await _apiClient.GetRecentObservationsAsync(
                        placeId.Value, daysAgo, page)).ToList();

                    if (!observations.Any())
                    {
                        _logger.LogInformation("No more observations on page {Page} for {RegionCode}.", page, item.RegionCode);
                        break;
                    }

                    foreach (var dto in observations)
                    {
                        var species = await GetOrCreateSpeciesAsync(dto);
                        var location = await GetOrCreateLocationAsync(dto, item.RegionCode, country);

                        // Deduplication: location ID is unique per iNat observation
                        var exists = await _dbContext.Observations.AnyAsync(o =>
                            o.LocationId == location.Id && o.Source == "inat");

                        if (!exists)
                        {
                            var observationDate = DateTime.TryParse(dto.ObservedOn, out var d)
                                ? d : DateTime.UtcNow;

                            _dbContext.Observations.Add(new Observation
                            {
                                Species = species,
                                Location = location,
                                ObservationDate = observationDate,
                                HowMany = Math.Max(1, dto.NumIdentificationAgreements),
                                Source = "inat"
                            });
                            totalIngested++;
                        }
                    }

                    await _dbContext.SaveChangesAsync();

                    if (observations.Count < 200) break; // Last page
                }

                item.Status = IngestionStatus.Completed;
                _logger.LogInformation("Region {RegionCode} done. {Count} observations ingested.", item.RegionCode, totalIngested);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing iNat region {RegionCode}", item.RegionCode);
                item.Status = IngestionStatus.Failed;
            }
            finally
            {
                await _dbContext.SaveChangesAsync();
            }
        }

        _logger.LogInformation("iNat ingestion batch complete.");
    }

    private async Task<Country> GetOrCreateCountryAsync(string countryCode)
    {
        var country = await _dbContext.Countries.FindAsync(countryCode);
        if (country == null)
        {
            country = new Country { Code = countryCode, Name = countryCode };
            _dbContext.Countries.Add(country);
        }
        return country;
    }

    private async Task<Location> GetOrCreateLocationAsync(INatObservationDto dto, string countryCode, Country country)
    {
        var locationId = $"inat-{dto.Id}";

        var location = await _dbContext.Locations.FindAsync(locationId);
        if (location == null)
        {
            // GeoJSON coordinates are [longitude, latitude]
            var lng = dto.Geojson!.Coordinates![0];
            var lat = dto.Geojson!.Coordinates![1];

            location = new Location
            {
                Id = locationId,
                Name = !string.IsNullOrWhiteSpace(dto.PlaceGuess) ? dto.PlaceGuess : "iNaturalist Location",
                Latitude = lat,
                Longitude = lng,
                CountryCode = countryCode,
                Country = country
            };
            _dbContext.Locations.Add(location);
        }
        return location;
    }

    private async Task<Species> GetOrCreateSpeciesAsync(INatObservationDto dto)
    {
        var taxon = dto.Taxon;
        // Primary key: ScientificName — bridges iNat and eBird taxonomy
        var species = _dbContext.Species.Local.FirstOrDefault(s => s.ScientificName == taxon.Name)
                   ?? await _dbContext.Species.FirstOrDefaultAsync(s => s.ScientificName == taxon.Name);

        if (species == null)
        {
            var family = taxon.Family;
            var order = taxon.Order;

            // iNat Observations API often omits family/order in the root taxon.
            // We extract them from the ancestors array in the identifications.
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(order))
            {
                var ancestors = dto.Identifications?.FirstOrDefault()?.Taxon?.Ancestors;
                if (ancestors != null)
                {
                    family ??= ancestors.FirstOrDefault(a => a.Rank == "family")?.Name;
                    order ??= ancestors.FirstOrDefault(a => a.Rank == "order")?.Name;
                }
            }

            species = new Species
            {
                SpeciesCode = $"inat-{taxon.Id}",
                ScientificName = taxon.Name,
                Order = order,
                FamilySciName = family
            };

            // Add English common name if available
            if (!string.IsNullOrWhiteSpace(taxon.PreferredCommonName))
            {
                species.Translations.Add(new SpeciesTranslation
                {
                    Locale = "en",
                    CommonName = taxon.PreferredCommonName
                });
            }

            // Fetch additional localized names in parallel
            var locales = new[] { "es", "pt", "fr", "de", "zh" };
            var tasks = locales.Select(async locale =>
            {
                var name = await _apiClient.GetTaxonLocalizedNameAsync(taxon.Id, locale);
                if (!string.IsNullOrWhiteSpace(name) && name != taxon.PreferredCommonName)
                    return new SpeciesTranslation { Locale = locale, CommonName = name };
                return null;
            });

            var results = await Task.WhenAll(tasks);
            foreach (var t in results.Where(r => r != null))
                species.Translations.Add(t!);

            _dbContext.Species.Add(species);
        }

        return species;
    }
}
