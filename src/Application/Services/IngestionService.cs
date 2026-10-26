using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

public class IngestionService : IIngestionService
{
    private readonly IEbirdApiClient _apiClient;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(IEbirdApiClient apiClient, IApplicationDbContext dbContext, ILogger<IngestionService> logger)
    {
        _apiClient = apiClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> ProcessQueueAsync()
    {
        const int batchSize = 10; // Process 10 regions at a time
        _logger.LogInformation("Inicio del proceso de ingestión desde la cola. Lote de {BatchSize} regiones.", batchSize);

        // Get a batch of items to process
        var queueItems = await _dbContext.IngestionQueue
            .Where(i => i.Source == "ebird" &&
                       (i.Status == IngestionStatus.Queued || i.Status == IngestionStatus.Failed) &&
                       i.AttemptCount < IngestionQueue.MaxAttempts)
            .OrderBy(i => i.LastAttemptUtc)
            .Take(batchSize)
            .ToListAsync();

        if (!queueItems.Any())
        {
            _logger.LogInformation("No hay regiones en la cola para procesar.");
            return 0;
        }

        var saved = 0;

        // Lock the items
        foreach (var item in queueItems)
        {
            item.Status = IngestionStatus.Processing;
            item.LastAttemptUtc = DateTime.UtcNow;
            item.AttemptCount++;
        }
        await _dbContext.SaveChangesAsync();

        foreach (var item in queueItems)
        {
            _logger.LogInformation("Procesando región: {RegionCode}", item.RegionCode);

            try
            {
                var country = await GetOrCreateCountryAsync(item.RegionCode);
                var observationsDto = await _apiClient.GetRecentObservationsAsync(item.RegionCode);

                if (!observationsDto.Any())
                {
                    _logger.LogWarning("No se encontraron observaciones para la región: {RegionCode}", item.RegionCode);
                    item.Status = IngestionStatus.Completed; // Or a different status if you want to track this
                    continue;
                }

                saved += await SaveObservationsAsync(observationsDto, country);

                item.Status = IngestionStatus.Completed;
                _logger.LogInformation("Región {RegionCode} procesada y datos guardados exitosamente.", item.RegionCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió un error procesando la región {RegionCode}", item.RegionCode);
                item.Status = IngestionStatus.Failed;
            }
            finally
            {
                await _dbContext.SaveChangesAsync();
            }
        }

        _logger.LogInformation("Proceso de ingestión finalizado: {Saved} observaciones nuevas.", saved);
        return saved;
    }

    /// <summary>
    /// eBird's historic endpoint, day by day: fills a gap the recent endpoint (at most 30 days back) cannot reach,
    /// like the four months the queue was frozen in 2026. One call per region and day; each finished day is recorded
    /// in SyncStates ("ebird-historic:yyyyMMdd"), so a stopped run resumes where it left off. A region that keeps
    /// failing on a day is retried twice, then logged and left out of that day.
    /// </summary>
    public async Task<int> BackfillHistoricAsync(IReadOnlyList<string> regions, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var saved = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var key = $"ebird-historic:{day:yyyyMMdd}";
            if (await _dbContext.SyncStates.AnyAsync(x => x.Key == key, cancellationToken)) continue;

            var pending = regions.ToList();
            var daySaved = 0;
            for (var pass = 0; pass < 3 && pending.Count > 0; pass++)
            {
                var failed = new List<string>();
                foreach (var region in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var country = await GetOrCreateCountryAsync(region);
                        var observations = await FetchHistoricAsync(region, day);
                        daySaved += await SaveObservationsAsync(observations, country);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        // Saved: let go of the region's entities, so the next save doesn't walk the whole run.
                        _dbContext.ChangeTracker.Clear();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning("Historic {Region} {Day:yyyy-MM-dd} failed: {Message}", region, day, ex.Message);
                        DiscardPendingChanges();
                        failed.Add(region);
                    }
                }
                pending = failed;
            }

            if (pending.Count > 0)
                _logger.LogWarning("Historic {Day:yyyy-MM-dd}: left out {Count} regions: {Regions}", day, pending.Count, string.Join(" ", pending));
            _dbContext.SyncStates.Add(new SyncState { Key = key, LastSyncValue = daySaved, UpdatedAt = DateTime.UtcNow });
            await _dbContext.SaveChangesAsync(cancellationToken);
            saved += daySaved;
            _logger.LogInformation("Historic {Day:yyyy-MM-dd}: {Saved} new observations.", day, daySaved);
        }
        return saved;
    }

    /// <summary>Regions eBird can't serve in one historic call, learned once per run so later days go straight down.</summary>
    private readonly HashSet<string> _tooLarge = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _subregions = new();

    /// <summary>
    /// One region's day. When eBird gives up on the region (a whole Australia or California), its states are asked
    /// for instead, and a state's counties if needed (two levels at most).
    /// </summary>
    private async Task<List<EbirdObservationDto>> FetchHistoricAsync(string region, DateOnly day, int depth = 0)
    {
        if (!_tooLarge.Contains(region))
        {
            try { return (await _apiClient.GetHistoricObservationsAsync(region, day)).ToList(); }
            catch (RegionTooLargeException) when (depth < 2)
            {
                _logger.LogInformation("Historic {Region}: too large for one call; asking for its subregions.", region);
                _tooLarge.Add(region);
            }
        }
        if (!_subregions.TryGetValue(region, out var parts))
            _subregions[region] = parts = await _apiClient.GetSubregionsAsync(region);
        if (parts.Count == 0) throw new RegionTooLargeException(region);

        var all = new List<EbirdObservationDto>();
        foreach (var part in parts) all.AddRange(await FetchHistoricAsync(part, day, depth + 1));
        return all;
    }

    /// <summary>Adds the observations not stored yet (same location, species and time); returns how many.</summary>
    /// <remarks>Duplicates within the batch are caught by a set keyed on the species code (a new species has no id
    /// yet), not by scanning the tracked observations: that scan grew with every observation of the run and made the
    /// historic backfill quadratic (7 hours for its second day).</remarks>
    private async Task<int> SaveObservationsAsync(IEnumerable<EbirdObservationDto> observations, Country country)
    {
        var saved = 0;
        var added = new HashSet<(string LocationId, string SpeciesCode, DateTime At)>();
        foreach (var dto in observations)
        {
            var location = await GetOrCreateLocationAsync(dto, country);
            var species = await GetOrCreateSpeciesAsync(dto);
            var observedAt = DateTime.Parse(dto.ObsDt);

            if (!added.Add((location.Id, species.SpeciesCode, observedAt))) continue;
            if (species.Id != 0 && await _dbContext.Observations.AnyAsync(o =>
                    o.LocationId == location.Id && o.SpeciesId == species.Id && o.ObservationDate == observedAt))
                continue;

            _dbContext.Observations.Add(new Observation
            {
                HowMany = dto.HowMany,
                ObservationDate = observedAt,
                Location = location,
                Species = species
            });
            saved++;
        }
        return saved;
    }

    /// <summary>After a failed region, drop what it half-added so the next save doesn't carry it.</summary>
    private void DiscardPendingChanges()
    {
        if (_dbContext is Microsoft.EntityFrameworkCore.DbContext context) context.ChangeTracker.Clear();
    }

    private async Task<Country> GetOrCreateCountryAsync(string countryCode)
    {
        // FindAsync primero revisa el tracker local, por lo que es seguro para este caso.
        var country = await _dbContext.Countries.FindAsync(countryCode);
        if (country == null)
        {
            country = new Country { Code = countryCode, Name = countryCode };
            _dbContext.Countries.Add(country);
        }
        return country;
    }

    private async Task<State?> GetOrCreateStateAsync(EbirdLocationDetailsDto details, Country country)
    {
        if (string.IsNullOrEmpty(details.StateCode) || string.IsNullOrEmpty(details.StateName))
        {
            return null;
        }

        // Primero buscar en el tracker local, luego en la base de datos.
        var state = _dbContext.States.Local.FirstOrDefault(s => s.Code == details.StateCode)
                        ?? await _dbContext.States.FirstOrDefaultAsync(s => s.Code == details.StateCode);

        if (state == null)
        {
            state = new State
            {
                Code = details.StateCode,
                Name = details.StateName,
                Country = country
            };
            _dbContext.States.Add(state);
        }
        return state;
    }

    private async Task<Location> GetOrCreateLocationAsync(EbirdObservationDto dto, Country country)
    {
        // FindAsync es seguro porque revisa el tracker local primero.
        var location = await _dbContext.Locations.FindAsync(dto.LocId);
        if (location == null)
        {
            // Historic (detail=full) rows carry their state; only the recent endpoint needs a lookup per new site.
            var details = dto.StateCode is { Length: > 0 }
                ? new EbirdLocationDetailsDto { StateCode = dto.StateCode, StateName = dto.StateName }
                : await _apiClient.GetLocationDetailsAsync(dto.LocId);
            var state = await GetOrCreateStateAsync(details, country);

            location = new Location
            {
                Id = dto.LocId,
                Name = dto.LocName,
                Latitude = dto.Lat,
                Longitude = dto.Lng,
                Country = country,
                State = state
            };
            _dbContext.Locations.Add(location);
        }
        return location;
    }

    private async Task<Species> GetOrCreateSpeciesAsync(EbirdObservationDto dto)
    {
        // Primero buscar en el tracker local, luego en la base de datos.
        var species = _dbContext.Species.Local.FirstOrDefault(s => s.SpeciesCode == dto.SpeciesCode)
                          ?? await _dbContext.Species.FirstOrDefaultAsync(s => s.SpeciesCode == dto.SpeciesCode);

        if (species == null)
        {
            species = new Species
            {
                SpeciesCode = dto.SpeciesCode,
                ScientificName = dto.SciName
            };

            // English is the default from the observation DTO
            species.Translations.Add(new SpeciesTranslation { Locale = "en", CommonName = dto.ComName });

            // Fetch additional languages in parallel
            var locales = new[] { "es", "pt", "fr" };
            var tasks = locales.Select(async locale => 
            {
                var localizedName = await _apiClient.GetSpeciesLocalizedNameAsync(dto.SpeciesCode, locale);
                if (!string.IsNullOrEmpty(localizedName) && localizedName != dto.ComName) 
                {
                    return new SpeciesTranslation { Locale = locale, CommonName = localizedName };
                }
                return null;
            });

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                if (result != null)
                {
                    species.Translations.Add(result);
                }
            }

            _dbContext.Species.Add(species);
        }
        return species;
    }
}