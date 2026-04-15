using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Flickr.Geo.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Flickr.Geo.Infrastructure.Services;

public class FlickrGeoService : IFlickrGeoService
{
    private const int BatchSize = 1500;
    private const int MaxConcurrency = 10;   // max simultaneous Flickr API calls
    private const int MaxRetries = 4;

    private readonly EbirdIngestorDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlickrGeoService> _logger;

    public FlickrGeoService(
        EbirdIngestorDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FlickrGeoService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task GeolocatePhotosAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["FlickrApi:ApiKey"]
            ?? throw new InvalidOperationException("FlickrApi:ApiKey is not configured.");

        var photos = await _db.FlickrPhotos
            .Where(p => p.Location == null)
            .Select(p => new { p.Id, p.PhotoId })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Found {Count} photos without geolocation.", photos.Count);

        var client = _httpClientFactory.CreateClient("Flickr");
        var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var totalUpdated = 0;

        for (int i = 0; i < photos.Count; i += BatchSize)
        {
            var batch = photos.Skip(i).Take(BatchSize).ToList();
            _logger.LogInformation("Processing geo batch {Start}-{End} of {Total}...",
                i + 1, i + batch.Count, photos.Count);

            var tasks = batch.Select(async photo =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await GetLocationAsync(client, photo.Id, photo.PhotoId, apiKey, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = (await Task.WhenAll(tasks)).Where(r => r is not null).ToList();

            if (results.Count > 0)
            {
                await _db.FlickrPhotoLocations.AddRangeAsync(results!, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                totalUpdated += results.Count;
                _logger.LogInformation("Saved {Count} geo records from batch.", results.Count);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("Geo worker complete. Total geolocated: {Count}", totalUpdated);
    }

    private async Task<FlickrPhotoLocation?> GetLocationAsync(
        HttpClient client,
        int photoDbId,
        long flickrPhotoId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"https://www.flickr.com/services/rest/" +
                  $"?method=flickr.photos.geo.getLocation" +
                  $"&api_key={apiKey}" +
                  $"&photo_id={flickrPhotoId}" +
                  $"&format=json&nojsoncallback=1";

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var response = await client.GetAsync(url, cancellationToken);

                if ((int)response.StatusCode == 429)
                {
                    var wait = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    _logger.LogWarning("429 on PhotoId={Id}. Waiting {S}s.", flickrPhotoId, wait.TotalSeconds);
                    await Task.Delay(wait, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "ok")
                    return null;

                var location = root.GetProperty("photo").GetProperty("location");

                return new FlickrPhotoLocation
                {
                    FlickrPhotoId = photoDbId,
                    Latitude = GetDouble(location, "latitude"),
                    Longitude = GetDouble(location, "longitude"),
                    Accuracy = GetInt(location, "accuracy"),
                    Context = GetInt(location, "context"),
                    PlaceId = GetString(location, "place_id"),
                    WoeId = GetLong(location, "woeid"),
                    LocalityName = GetNestedString(location, "locality", "_content"),
                    LocalityPlaceId = GetNestedString(location, "locality", "place_id"),
                    LocalityWoeId = GetNestedLong(location, "locality", "woeid"),
                    CountyName = GetNestedString(location, "county", "_content"),
                    CountyPlaceId = GetNestedString(location, "county", "place_id"),
                    CountyWoeId = GetNestedLong(location, "county", "woeid"),
                    RegionName = GetNestedString(location, "region", "_content"),
                    RegionPlaceId = GetNestedString(location, "region", "place_id"),
                    RegionWoeId = GetNestedLong(location, "region", "woeid"),
                    CountryName = GetNestedString(location, "country", "_content"),
                    CountryPlaceId = GetNestedString(location, "country", "place_id"),
                    CountryWoeId = GetNestedLong(location, "country", "woeid"),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Attempt {A} failed for PhotoId={Id}: {Msg}", attempt + 1, flickrPhotoId, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static int? GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i2)) return i2;
        return null;
    }

    private static long? GetLong(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var l2)) return l2;
        return null;
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static string? GetNestedString(JsonElement el, string parent, string child)
        => el.TryGetProperty(parent, out var p) && p.TryGetProperty(child, out var c) ? c.GetString() : null;

    private static long? GetNestedLong(JsonElement el, string parent, string child)
    {
        if (!el.TryGetProperty(parent, out var p) || !p.TryGetProperty(child, out var c)) return null;
        if (c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var l)) return l;
        if (c.ValueKind == JsonValueKind.String && long.TryParse(c.GetString(), out var l2)) return l2;
        return null;
    }
}
