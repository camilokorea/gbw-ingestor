using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

public interface IINatPhotoIngestionService
{
    Task ProcessSpeciesPhotosAsync(CancellationToken cancellationToken = default);
}

public class INatPhotoIngestionService : IINatPhotoIngestionService
{
    private readonly IApplicationDbContext _db;
    private readonly IINatPhotoService _iNatPhotoService;
    private readonly ILogger<INatPhotoIngestionService> _logger;

    public INatPhotoIngestionService(
        IApplicationDbContext db,
        IINatPhotoService iNatPhotoService,
        ILogger<INatPhotoIngestionService> logger)
    {
        _db = db;
        _iNatPhotoService = iNatPhotoService;
        _logger = logger;
    }

    public async Task ProcessSpeciesPhotosAsync(CancellationToken cancellationToken = default)
    {
        // Find species that have no iNat photos yet — regardless of whether they have a TaxonId yet
        var speciesToFix = await _db.Species
            .Where(s => !_db.SpeciesPhotos.Any(sp => sp.SpeciesId == s.Id))
            .Where(s => !_db.SpeciesINatPhotos.Any(sp => sp.SpeciesId == s.Id))
            .OrderByDescending(s => s.ObservationCount)
            .Take(100)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} species needing iNat photos.", speciesToFix.Count);

        foreach (var species in speciesToFix)
        {
            // Resolve taxon ID from scientific name if we don't have it yet
            if (species.INatTaxonId == null)
            {
                _logger.LogInformation("Resolving iNat taxon for {ScientificName}...", species.ScientificName);
                var taxonId = await _iNatPhotoService.SearchTaxonIdByNameAsync(species.ScientificName, cancellationToken);

                if (taxonId == null)
                {
                    _logger.LogWarning("iNat taxon not found for {ScientificName}. Skipping.", species.ScientificName);
                    await Task.Delay(500, cancellationToken); // polite delay
                    continue;
                }

                // Cache it back so future runs skip the API lookup
                species.INatTaxonId = taxonId;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Fetching iNat photos for {ScientificName} (iNat:{TaxonId})", 
                species.ScientificName, species.INatTaxonId);

            var photos = await _iNatPhotoService.GetPhotosForTaxonAsync(species.INatTaxonId!.Value, cancellationToken);

            if (photos.Count == 0)
            {
                _logger.LogWarning("No iNat photos found for {ScientificName}", species.ScientificName);
                continue;
            }

            foreach (var photo in photos)
            {
                var existingPhoto = await _db.INatPhotos
                    .FirstOrDefaultAsync(p => p.PhotoId == photo.PhotoId, cancellationToken);

                if (existingPhoto == null)
                {
                    _db.INatPhotos.Add(photo);
                    await _db.SaveChangesAsync(cancellationToken);
                    existingPhoto = photo;
                }

                if (!await _db.SpeciesINatPhotos.AnyAsync(sp => sp.SpeciesId == species.Id && sp.INatPhotoId == existingPhoto.Id, cancellationToken))
                {
                    _db.SpeciesINatPhotos.Add(new SpeciesINatPhoto
                    {
                        SpeciesId = species.Id,
                        INatPhotoId = existingPhoto.Id,
                        Rank = 0
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Added {Count} photos for {ScientificName}", photos.Count, species.ScientificName);

            await Task.Delay(1000, cancellationToken); // polite iNat delay
        }
    }
}
