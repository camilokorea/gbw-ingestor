using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Flickr.Matcher.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flickr.Matcher.Infrastructure.Services;

public class FlickrMatcherService : IFlickrMatcherService
{
    private readonly EbirdIngestorDbContext _db;
    private readonly ILogger<FlickrMatcherService> _logger;

    public FlickrMatcherService(EbirdIngestorDbContext db, ILogger<FlickrMatcherService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task MatchPhotosToSpeciesAsync(CancellationToken cancellationToken = default)
    {
        // Build O(n) lookup: lowercase sciname → speciesId
        var speciesLookup = await _db.Species
            .Select(s => new { s.Id, SciName = s.ScientificName.ToLower() })
            .ToDictionaryAsync(s => s.SciName, s => s.Id, cancellationToken);

        _logger.LogInformation("Loaded {Count} species for matching.", speciesLookup.Count);

        // Only load photos not yet linked to any species
        var unlinkedPhotos = await _db.FlickrPhotos
            .Where(p => !_db.SpeciesPhotos.Any(sp => sp.FlickrPhotoId == p.Id))
            .Select(p => new { p.Id, Title = p.Title.ToLower() })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} unlinked photos to process.", unlinkedPhotos.Count);

        var newMatches = new List<SpeciesPhoto>();
        var matchedPairs = new HashSet<(int SpeciesId, int PhotoId)>();

        foreach (var photo in unlinkedPhotos)
        {
            foreach (var (sciName, speciesId) in speciesLookup)
            {
                if (photo.Title.Contains(sciName, StringComparison.OrdinalIgnoreCase))
                {
                    var key = (speciesId, photo.Id);
                    if (!matchedPairs.Contains(key))
                    {
                        matchedPairs.Add(key);
                        newMatches.Add(new SpeciesPhoto
                        {
                            SpeciesId = speciesId,
                            FlickrPhotoId = photo.Id
                        });
                    }
                }
            }
        }

        _logger.LogInformation("Found {Count} new species-photo matches.", newMatches.Count);

        if (newMatches.Count == 0)
        {
            _logger.LogInformation("No new matches found. Nothing to insert.");
            return;
        }

        await _db.SpeciesPhotos.AddRangeAsync(newMatches, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully inserted {Count} species-photo matches.", newMatches.Count);
    }
}
