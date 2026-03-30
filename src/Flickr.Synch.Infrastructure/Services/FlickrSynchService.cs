using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Flickr.Synch.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Flickr.Synch.Infrastructure.Services;

public class FlickrSynchService : IFlickrSynchService
{
    private readonly EbirdIngestorDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlickrSynchService> _logger;

    public FlickrSynchService(
        EbirdIngestorDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FlickrSynchService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["FlickrApi:ApiKey"]
            ?? throw new InvalidOperationException("FlickrApi:ApiKey is not configured.");
        var pageSize = int.Parse(_configuration["FlickrApi:PageSize"] ?? "500");
        var groups = _configuration.GetSection("FlickrApi:Groups").Get<string[]>()
            ?? throw new InvalidOperationException("FlickrApi:Groups is not configured.");

        // Load existing PhotoIds into a HashSet for O(1) dedup
        var existingPhotoIds = await _db.FlickrPhotos
            .Select(p => p.PhotoId)
            .ToHashSetAsync(cancellationToken);

        _logger.LogInformation("Loaded {Count} existing photos from database.", existingPhotoIds.Count);

        var totalInserted = 0;

        foreach (var groupId in groups)
        {
            _logger.LogInformation("Processing Flickr group: {GroupId}", groupId);
            var inserted = await ProcessGroupAsync(groupId, apiKey, pageSize, existingPhotoIds, cancellationToken);
            totalInserted += inserted;
        }

        _logger.LogInformation("Sync complete. Total photos inserted: {Count}", totalInserted);
    }

    private async Task<int> ProcessGroupAsync(
        string groupId,
        string apiKey,
        int pageSize,
        HashSet<long> existingPhotoIds,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Flickr");
        var page = 1;
        var inserted = 0;
        var hasMore = true;

        while (hasMore)
        {
            var url = $"https://www.flickr.com/services/rest/" +
                      $"?method=flickr.groups.pools.getPhotos" +
                      $"&api_key={apiKey}" +
                      $"&group_id={groupId}" +
                      $"&extras=url_sq,url_t,url_s,url_q,url_m,url_n,url_z,url_c,url_l,url_o,owner_name,path_alias" +
                      $"&per_page={pageSize}" +
                      $"&page={page}" +
                      $"&format=json&nojsoncallback=1";

            _logger.LogInformation("Fetching group={GroupId} page={Page}", groupId, page);

            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            var photosNode = doc.RootElement.GetProperty("photos");
            var photos = photosNode.GetProperty("photo");

            if (photos.GetArrayLength() == 0)
            {
                hasMore = false;
                break;
            }

            var newPhotos = new List<FlickrPhoto>();

            foreach (var photo in photos.EnumerateArray())
            {
                var photoId = long.Parse(photo.GetProperty("id").GetString()!);

                if (existingPhotoIds.Contains(photoId))
                    continue;

                var entity = new FlickrPhoto
                {
                    PhotoId = photoId,
                    Title = GetString(photo, "title"),
                    OwnerName = GetString(photo, "ownername"),
                    UserId = GetString(photo, "owner"),
                    SquareThumbnailUrl = GetString(photo, "url_sq"),
                    LargeSquareThumbnailUrl = GetString(photo, "url_q"),
                    ThumbnailUrl = GetString(photo, "url_t"),
                    SmallUrl = GetString(photo, "url_s"),
                    Small320Url = GetString(photo, "url_n"),
                    MediumUrl = GetString(photo, "url_m"),
                    Medium640Url = GetString(photo, "url_z"),
                    Medium800Url = GetString(photo, "url_c"),
                    LargeUrl = GetString(photo, "url_l"),
                    WebUrl = $"https://www.flickr.com/photos/{GetString(photo, "owner")}/{photoId}"
                };

                newPhotos.Add(entity);
                existingPhotoIds.Add(photoId);
            }

            if (newPhotos.Count > 0)
            {
                await _db.FlickrPhotos.AddRangeAsync(newPhotos, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                inserted += newPhotos.Count;
                _logger.LogInformation("Inserted {Count} new photos from page {Page}.", newPhotos.Count, page);
            }

            page++;
            cancellationToken.ThrowIfCancellationRequested();
        }

        return inserted;
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var val))
            return val.ValueKind == JsonValueKind.String ? val.GetString() ?? string.Empty : string.Empty;
        return string.Empty;
    }
}
