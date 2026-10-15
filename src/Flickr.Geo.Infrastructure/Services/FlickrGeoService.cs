using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Flickr.Geo.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Flickr.Geo.Infrastructure.Services;

/// <summary>
/// Asks Flickr (flickr.photos.geo.getLocation) for the location of photos not checked yet, one request at a time at
/// a fixed pace, and remembers the photos that have none. It shares the API key with flickr-synch and the license
/// backfill, whose budget is about 3,600 calls an hour.
/// </summary>
/// <remarks>
/// It used to send 10 requests at a time with no pacing and never record "no location", so every run re-asked about
/// the ~320k photos without one: ~11,500 calls an hour, more than half answered 429, and nothing saved.
/// </remarks>
public class FlickrGeoService : IFlickrGeoService
{
    private const int SaveEvery = 200;

    private readonly EbirdIngestorDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlickrGeoService> _logger;

    /// <summary>Pause between requests (FlickrGeo:RequestsPerHour, default 2,400, leaves room for the other workers).</summary>
    public TimeSpan RequestInterval { get; set; }
    /// <summary>Photos per run (FlickrGeo:MaxPerRun): about 5 h at the default pace, so a run ends before the next one.</summary>
    public int MaxPerRun { get; set; }
    /// <summary>Wait after a 429 without Retry-After; doubles on each consecutive one.</summary>
    public TimeSpan ThrottleBackoff { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Consecutive 429s after which the run stops: the key's hourly budget is spent, retrying only burns it.</summary>
    public int MaxConsecutiveThrottles { get; set; } = 5;
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

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
        var perHour = Math.Max(1, configuration.GetValue("FlickrGeo:RequestsPerHour", 2400));
        RequestInterval = TimeSpan.FromSeconds(3600.0 / perHour);
        MaxPerRun = configuration.GetValue("FlickrGeo:MaxPerRun", 12000);
    }

    private enum Outcome { Located, NoLocation, Throttled, Transient }

    public async Task<GeoRunReport> GeolocatePhotosAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["FlickrApi:ApiKey"]
            ?? throw new InvalidOperationException("FlickrApi:ApiKey is not configured.");

        // Species-linked photos first (the ones the app shows), newest first within each group.
        var photos = await _db.FlickrPhotos
            .Where(p => p.Location == null && p.GeoCheckedAt == null)
            .OrderByDescending(p => p.SpeciesPhotos.Any())
            .ThenByDescending(p => p.Id)
            .Take(MaxPerRun)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("{Count} photos to check this run (pace: one every {Interval}s).",
            photos.Count, RequestInterval.TotalSeconds);

        var client = _httpClientFactory.CreateClient("Flickr");
        int located = 0, withoutLocation = 0, skipped = 0, sinceSave = 0, throttles = 0;
        var stopped = false;

        foreach (var photo in photos)
        {
            var (outcome, location, retryAfter) = await GetLocationAsync(client, photo, apiKey, cancellationToken);
            switch (outcome)
            {
                case Outcome.Located:
                    _db.FlickrPhotoLocations.Add(location!);
                    photo.GeoCheckedAt = UtcNow();
                    located++;
                    throttles = 0;
                    break;
                case Outcome.NoLocation:
                    photo.GeoCheckedAt = UtcNow();
                    withoutLocation++;
                    throttles = 0;
                    break;
                case Outcome.Throttled:
                    skipped++;
                    if (++throttles >= MaxConsecutiveThrottles)
                    {
                        _logger.LogWarning("Flickr answered 429 {Count} times in a row; stopping this run.", throttles);
                        stopped = true;
                    }
                    else
                    {
                        var wait = retryAfter ?? ThrottleBackoff * Math.Pow(2, throttles - 1);
                        _logger.LogWarning("Flickr answered 429; pausing {Seconds}s.", wait.TotalSeconds);
                        await Task.Delay(wait, cancellationToken);
                    }
                    break;
                default:
                    skipped++;
                    break;
            }
            if (stopped) break;

            if (++sinceSave >= SaveEvery)
            {
                await _db.SaveChangesAsync(cancellationToken);
                sinceSave = 0;
                _logger.LogInformation("Geo progress: {Located} located, {None} without location, {Skipped} skipped.",
                    located, withoutLocation, skipped);
            }
            await Task.Delay(RequestInterval, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Geo run complete: {Located} located, {None} without location, {Skipped} skipped{Stopped}.",
            located, withoutLocation, skipped, stopped ? " (stopped: throttled)" : "");
        return new GeoRunReport(located, withoutLocation, skipped, stopped);
    }

    private async Task<(Outcome, FlickrPhotoLocation?, TimeSpan?)> GetLocationAsync(
        HttpClient client, FlickrPhoto photo, string apiKey, CancellationToken cancellationToken)
    {
        var url = "https://www.flickr.com/services/rest/" +
                  "?method=flickr.photos.geo.getLocation" +
                  $"&api_key={apiKey}&photo_id={photo.PhotoId}&format=json&nojsoncallback=1";
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if ((int)response.StatusCode is 429 or 503)
                return (Outcome.Throttled, null, response.Headers.RetryAfter?.Delta);
            if (!response.IsSuccessStatusCode) return (Outcome.Transient, null, null);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.TryGetProperty("stat", out var stat) && stat.GetString() == "ok")
                return (Outcome.Located, ToLocation(photo.Id, root.GetProperty("photo").GetProperty("location")), null);

            var code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
            return code switch
            {
                // 1 = photo not found / not viewable, 2 = no location: definitive answers, never ask again.
                1 or 2 => (Outcome.NoLocation, null, null),
                // 100 = invalid API key: every request will fail, so stop the run loudly.
                100 => throw new InvalidOperationException("Flickr rejected the API key (code 100)."),
                _ => (Outcome.Transient, null, null),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("Geo lookup failed for PhotoId={Id}: {Message}", photo.PhotoId, ex.Message);
            return (Outcome.Transient, null, null);
        }
    }

    private static FlickrPhotoLocation ToLocation(int photoDbId, JsonElement location) =>
        new FlickrPhotoLocation
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
