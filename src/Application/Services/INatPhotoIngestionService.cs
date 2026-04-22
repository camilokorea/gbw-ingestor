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
        // Find species that don't have ANY photos (Flickr or iNat)
        // AND have an iNat TaxonId
        var speciesToFix = await _db.Species
            .Where(s => s.INatTaxonId != null)
            .Where(s => !_db.SpeciesPhotos.Any(sp => sp.SpeciesId == s.Id))
            .Where(s => !_db.SpeciesINatPhotos.Any(sp => sp.SpeciesId == s.Id))
            .OrderByDescending(s => s.ObservationCount) // Prioritize popular birds
            .Take(100) // Process in batches
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} species needing iNat photos.", speciesToFix.Count);

        foreach (var species in speciesToFix)
        {
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
                // Check if we already have this photo ID in our global INatPhotos table
                var existingPhoto = await _db.INatPhotos
                    .FirstOrDefaultAsync(p => p.PhotoId == photo.PhotoId, cancellationToken);

                if (existingPhoto == null)
                {
                    _db.INatPhotos.Add(photo);
                    await _db.SaveChangesAsync(cancellationToken);
                    existingPhoto = photo;
                }

                // Link it to the species
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
            _logger.LogInformation("Successfully added {Count} photos for {ScientificName}", photos.Count, species.ScientificName);

            // Polite delay to avoid hitting iNat too hard
            await Task.Delay(1000, cancellationToken);
        }
    }
}
