using eBird.Ingestor.Domain;

namespace eBird.Ingestor.Application.Contracts;

public interface IINatPhotoService
{
    /// <summary>
    /// Fetches high-quality photos for a specific iNaturalist taxon ID.
    /// Returns a list of INatPhoto entities populated with iNat metadata.
    /// </summary>
    Task<List<INatPhoto>> GetPhotosForTaxonAsync(long taxonId, CancellationToken cancellationToken = default);
}
