using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Flickr.Synch.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Flickr.Synch.Infrastructure.Services;

/// <summary>
/// Two passes over photos with no license yet:
/// 1. Re-page every configured group pool asking only for extras=license (500 photos per call), which covers most
///    photos in a few hundred calls.
/// 2. For species-linked photos still unknown (left the pool, or beyond what the pool listing returns), one
///    flickr.photos.getInfo call each, up to a limit.
/// Then it reports coverage, so the license policy for social posts can be decided on real numbers.
/// </summary>
public class FlickrLicenseBackfill : IFlickrLicenseBackfill
{
    private readonly EbirdIngestorDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlickrLicenseBackfill> _logger;

    /// <summary>Pause between pool pages (same courtesy delay as the sync).</summary>
    public TimeSpan PageDelay { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Pause between getInfo calls: Flickr allows ~3600 calls/hour per key.</summary>
    public TimeSpan PhotoInfoDelay { get; set; } = TimeSpan.FromMilliseconds(1100);

    public FlickrLicenseBackfill(
        EbirdIngestorDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FlickrLicenseBackfill> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LicenseCoverageReport> RunAsync(int photoInfoLimit, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["FlickrApi:ApiKey"]
            ?? throw new InvalidOperationException("FlickrApi:ApiKey is not configured.");
        var pageSize = int.Parse(_configuration["FlickrApi:PageSize"] ?? "500");
        var groups = _configuration.GetSection("FlickrApi:Groups").Get<string[]>()
            ?? throw new InvalidOperationException("FlickrApi:Groups is not configured.");

        var pending = await _db.FlickrPhotos
            .Where(p => p.LicenseId == null)
            .Select(p => p.PhotoId)
            .ToHashSetAsync(cancellationToken);
        _logger.LogInformation("{Count} photos have no license yet.", pending.Count);

        var poolUpdates = 0;
        foreach (var groupId in groups)
        {
            if (pending.Count == 0) break;
            poolUpdates += await BackfillFromPoolAsync(groupId, apiKey, pageSize, pending, cancellationToken);
        }

        var infoUpdates = await BackfillFromPhotoInfoAsync(apiKey, photoInfoLimit, cancellationToken);

        var report = await BuildReportAsync(poolUpdates, infoUpdates, cancellationToken);
        LogReport(report);
        return report;
    }

    private async Task<int> BackfillFromPoolAsync(
        string groupId, string apiKey, int pageSize, HashSet<long> pending, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Flickr");
        var updated = 0;

        for (var page = 1; pending.Count > 0; page++)
        {
            var url = "https://www.flickr.com/services/rest/" +
                      "?method=flickr.groups.pools.getPhotos" +
                      $"&api_key={apiKey}&group_id={groupId}&extras=license" +
                      $"&per_page={pageSize}&page={page}&format=json&nojsoncallback=1";

            using var response = await FlickrHttp.GetWithRetryAsync(client, url, _logger, cancellationToken);
            if (response is null) break;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "ok" ||
                !root.TryGetProperty("photos", out var container) ||
                !container.TryGetProperty("photo", out var photos) ||
                photos.GetArrayLength() == 0)
            {
                break;
            }

            // Group the page's photos by license so each license is one UPDATE ... WHERE PhotoId IN (...).
            var byLicense = new Dictionary<int, List<long>>();
            foreach (var photo in photos.EnumerateArray())
            {
                if (!long.TryParse(photo.GetProperty("id").GetString(), out var photoId) || !pending.Contains(photoId))
                    continue;
                if (FlickrSynchService.GetLicenseId(photo) is not { } licenseId) continue;

                (byLicense.TryGetValue(licenseId, out var ids) ? ids : byLicense[licenseId] = new()).Add(photoId);
            }

            foreach (var (licenseId, ids) in byLicense)
            {
                updated += await _db.FlickrPhotos
                    .Where(p => ids.Contains(p.PhotoId))
                    .ExecuteUpdateAsync(set => set.SetProperty(p => p.LicenseId, licenseId), cancellationToken);
                pending.ExceptWith(ids);
            }

            var totalPages = container.TryGetProperty("pages", out var pages) && pages.TryGetInt32(out var n) ? n : page;
            _logger.LogInformation("Group {GroupId} page {Page}/{Pages}: {Updated} licenses so far, {Pending} pending.",
                groupId, page, totalPages, updated, pending.Count);
            if (page >= totalPages) break;

            await Task.Delay(PageDelay, cancellationToken);
        }

        return updated;
    }

    private async Task<int> BackfillFromPhotoInfoAsync(string apiKey, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0) return 0;

        // Only photos that can actually appear in a post: the ones linked to a species.
        var photos = await _db.FlickrPhotos
            .Where(p => p.LicenseId == null && p.SpeciesPhotos.Any())
            .OrderBy(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (photos.Count == 0) return 0;

        _logger.LogInformation("Fetching the license of {Count} species-linked photos one by one.", photos.Count);
        var client = _httpClientFactory.CreateClient("Flickr");
        var updated = 0;

        foreach (var photo in photos)
        {
            var url = "https://www.flickr.com/services/rest/" +
                      $"?method=flickr.photos.getInfo&api_key={apiKey}&photo_id={photo.PhotoId}" +
                      "&format=json&nojsoncallback=1";

            using var response = await FlickrHttp.GetWithRetryAsync(client, url, _logger, cancellationToken);
            if (response is not null)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                // A deleted or private photo answers stat=fail; it stays unknown (never republished) and the
                // Eraser worker removes it once its image URL dies.
                if (doc.RootElement.TryGetProperty("photo", out var info) &&
                    FlickrSynchService.GetLicenseId(info) is { } licenseId)
                {
                    photo.LicenseId = licenseId;
                    updated++;
                    if (updated % 100 == 0)
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        _logger.LogInformation("getInfo: {Updated}/{Total}", updated, photos.Count);
                    }
                }
            }

            await Task.Delay(PhotoInfoDelay, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task<LicenseCoverageReport> BuildReportAsync(int poolUpdates, int infoUpdates, CancellationToken cancellationToken)
    {
        var flickr = await _db.SpeciesPhotos
            .Select(sp => new { sp.SpeciesId, sp.FlickrPhoto.LicenseId })
            .ToListAsync(cancellationToken);
        var inat = await _db.SpeciesINatPhotos
            .Select(sp => new { sp.SpeciesId, sp.INatPhoto.LicenseCode })
            .ToListAsync(cancellationToken);

        var linkedByReuse = flickr
            .Where(p => FlickrLicenses.Find(p.LicenseId) is not null)
            .GroupBy(p => FlickrLicenses.ReuseOf(p.LicenseId))
            .ToDictionary(g => g.Key, g => g.Count());
        var unknown = flickr.Count(p => FlickrLicenses.Find(p.LicenseId) is null);

        // One (species, reuse) pair per photo, from both sources.
        var reuseBySpecies = flickr
            .Where(p => FlickrLicenses.Find(p.LicenseId) is not null)
            .Select(p => (p.SpeciesId, Reuse: FlickrLicenses.ReuseOf(p.LicenseId)))
            .Concat(inat.Select(p => (p.SpeciesId, Reuse: INatLicenses.ReuseOf(p.LicenseCode))))
            .ToList();

        var anyPhoto = flickr.Select(p => p.SpeciesId).Concat(inat.Select(p => p.SpeciesId)).Distinct().Count();
        int SpeciesWith(params LicenseReuse[] allowed) =>
            reuseBySpecies.Where(p => allowed.Contains(p.Reuse)).Select(p => p.SpeciesId).Distinct().Count();

        return new LicenseCoverageReport(
            poolUpdates, infoUpdates, linkedByReuse, unknown, anyPhoto,
            SpeciesWith(LicenseReuse.Open),
            SpeciesWith(LicenseReuse.Open, LicenseReuse.NonCommercial));
    }

    private void LogReport(LicenseCoverageReport report)
    {
        _logger.LogInformation("Licenses filled: {Pool} from group pools, {Info} via getInfo.",
            report.PoolUpdates, report.PhotoInfoUpdates);
        foreach (var reuse in Enum.GetValues<LicenseReuse>())
        {
            _logger.LogInformation("Species-linked Flickr photos, {Reuse}: {Count}",
                reuse, report.LinkedPhotosByReuse.GetValueOrDefault(reuse));
        }
        _logger.LogInformation("Species-linked Flickr photos, unknown license: {Count}", report.LinkedPhotosUnknown);
        _logger.LogInformation(
            "Species with a photo (Flickr or iNat): {Any}. With an open-license photo: {Open}. Counting non-commercial too: {OpenOrNc}.",
            report.SpeciesWithAnyPhoto, report.SpeciesWithOpenPhoto, report.SpeciesWithOpenOrNonCommercialPhoto);
    }
}
