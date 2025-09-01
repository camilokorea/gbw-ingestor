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

    public async Task ProcessRegionsAsync(IEnumerable<string> regionCodes)
    {
        _logger.LogInformation("Inicio del proceso de ingestión.");

        foreach (var regionCode in regionCodes)
        {
            _logger.LogInformation("Procesando región: {RegionCode}", regionCode);

            try
            {
                var country = await GetOrCreateCountryAsync(regionCode);
                var observationsDto = await _apiClient.GetRecentObservationsAsync(regionCode);
                if (!observationsDto.Any())
                {
                    _logger.LogWarning("No se encontraron observaciones para la región: {RegionCode}", regionCode);
                    continue;
                }

                foreach (var dto in observationsDto)
                {
                    var location = await GetOrCreateLocationAsync(dto, country);
                    var species = await GetOrCreateSpeciesAsync(dto);

                    var observationExists = await _dbContext.Observations.AnyAsync(o => 
                        o.LocationId == location.Id && 
                        o.SpeciesId == species.Id && 
                        o.ObservationDate == DateTime.Parse(dto.ObsDt));

                    if (!observationExists)
                    {
                        var observation = new Observation
                        {
                            HowMany = dto.HowMany,
                            ObservationDate = DateTime.Parse(dto.ObsDt),
                            Location = location,
                            Species = species
                        };
                        _dbContext.Observations.Add(observation);
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Región {RegionCode} procesada y datos guardados exitosamente.", regionCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocurrió un error procesando la región {RegionCode}", regionCode);
            }
        }
        _logger.LogInformation("Proceso de ingestión finalizado.");
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
            var details = await _apiClient.GetLocationDetailsAsync(dto.LocId);
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
                CommonName = dto.ComName,
                ScientificName = dto.SciName
            };
            _dbContext.Species.Add(species);
        }
        return species;
    }
}