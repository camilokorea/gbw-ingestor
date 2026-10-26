using eBird.Ingestor.Application.Contracts.Dtos;

namespace eBird.Ingestor.Application.Contracts;

public interface IEbirdApiClient
{
    Task<IEnumerable<EbirdObservationDto>> GetRecentObservationsAsync(string regionCode);

    /// <summary>
    /// What was reported in a region on one day (eBird's historic endpoint, detail=full, so each row carries its
    /// state and no location lookup is needed). Observer names come with it and are never stored.
    /// </summary>
    /// <exception cref="RegionTooLargeException">eBird gave up on the region (it answers 503 after 60 s for a whole
    /// Australia or California day); ask for its subregions instead.</exception>
    Task<IEnumerable<EbirdObservationDto>> GetHistoricObservationsAsync(string regionCode, DateOnly date);

    /// <summary>A region's next level down: a country's states (subnational1), a state's counties (subnational2).</summary>
    Task<IReadOnlyList<string>> GetSubregionsAsync(string regionCode);
    Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId);
    Task<string?> GetSpeciesLocalizedNameAsync(string speciesCode, string locale = "es");
}

/// <summary>eBird answered a server error for a region too big to serve in one call.</summary>
public sealed class RegionTooLargeException(string regionCode) : Exception($"eBird could not serve {regionCode} in one call")
{
    public string RegionCode { get; } = regionCode;
}
