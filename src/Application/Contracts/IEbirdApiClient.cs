using eBird.Ingestor.Application.Contracts.Dtos;

namespace eBird.Ingestor.Application.Contracts;

public interface IEbirdApiClient
{
    Task<IEnumerable<EbirdObservationDto>> GetRecentObservationsAsync(string regionCode);

    /// <summary>
    /// What was reported in a region on one day (eBird's historic endpoint, detail=full, so each row carries its
    /// state and no location lookup is needed). Observer names come with it and are never stored.
    /// </summary>
    Task<IEnumerable<EbirdObservationDto>> GetHistoricObservationsAsync(string regionCode, DateOnly date);
    Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId);
    Task<string?> GetSpeciesLocalizedNameAsync(string speciesCode, string locale = "es");
}