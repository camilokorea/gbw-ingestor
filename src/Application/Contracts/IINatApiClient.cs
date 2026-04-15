using eBird.Ingestor.Application.Contracts.Dtos;

namespace eBird.Ingestor.Application.Contracts;

/// <summary>
/// HTTP client abstraction for the iNaturalist API.
/// Mirrors IEbirdApiClient in structure.
/// </summary>
public interface IINatApiClient
{
    /// <summary>
    /// Returns research-grade bird observations for an iNaturalist place ID.
    /// Handles pagination internally up to MaxPagesPerCountry.
    /// </summary>
    Task<IEnumerable<INatObservationDto>> GetRecentObservationsAsync(int inatPlaceId, int daysAgo, int page = 1);

    /// <summary>
    /// Returns the preferred common name for a taxon in the given locale.
    /// Used to populate SpeciesTranslations.
    /// </summary>
    Task<string?> GetTaxonLocalizedNameAsync(int taxonId, string locale);
}
