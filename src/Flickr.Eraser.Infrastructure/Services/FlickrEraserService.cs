using eBird.Ingestor.Infrastructure.Data;
using Flickr.Eraser.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flickr.Eraser.Infrastructure.Services;

public class FlickrEraserService : IFlickrEraserService
{
    private const int BatchSize = 1500;

    private readonly EbirdIngestorDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FlickrEraserService> _logger;

    public FlickrEraserService(
        EbirdIngestorDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<FlickrEraserService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task EraseDeletedPhotosAsync(CancellationToken cancellationToken = default)
    {
        var photos = await _db.FlickrPhotos
            .Select(p => new { p.Id, p.Small320Url })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Loaded {Total} photos to validate.", photos.Count);

        var toDelete = new List<int>();
        var client = _httpClientFactory.CreateClient("Flickr");

        // Process in batches of 1500 (parallel HEAD requests)
        for (int i = 0; i < photos.Count; i += BatchSize)
        {
            var batch = photos.Skip(i).Take(BatchSize).ToList();
            _logger.LogInformation("Checking batch {Start}-{End} of {Total}...", i + 1, i + batch.Count, photos.Count);

            var tasks = batch.Select(async photo =>
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Head, photo.Small320Url);
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Dead photo id={Id} url={Url} status={Status}", photo.Id, photo.Small320Url, (int)response.StatusCode);
                        return photo.Id;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error checking photo id={Id}: {Message}", photo.Id, ex.Message);
                    return photo.Id;
                }
                return (int?)null;
            });

            var results = await Task.WhenAll(tasks);
            toDelete.AddRange(results.Where(r => r.HasValue).Select(r => r!.Value));

            cancellationToken.ThrowIfCancellationRequested();
        }

        if (toDelete.Count == 0)
        {
            _logger.LogInformation("No dead photos found. Nothing to delete.");
            return;
        }

        _logger.LogInformation("Deleting {Count} dead photos from database.", toDelete.Count);

        // Hard delete — remove SpeciesPhotos first (FK constraint), then FlickrPhotos
        var speciesPhotos = _db.SpeciesPhotos.Where(sp => toDelete.Contains(sp.FlickrPhotoId));
        _db.SpeciesPhotos.RemoveRange(speciesPhotos);

        var flickrPhotos = _db.FlickrPhotos.Where(p => toDelete.Contains(p.Id));
        _db.FlickrPhotos.RemoveRange(flickrPhotos);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully deleted {Count} dead photos.", toDelete.Count);
    }
}
