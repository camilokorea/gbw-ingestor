using System.Net;
using System.Text.RegularExpressions;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

public interface ICommonsPhotoIngestionService
{
    Task<CommonsPhotoReport> SyncAsync(CancellationToken cancellationToken = default);
}

/// <param name="SpeciesLookedUp">Species whose Wikidata lookup succeeded.</param>
/// <param name="SpeciesOnWikidata">Of those, species with a species-rank Wikidata item.</param>
/// <param name="SpeciesWithImage">Of those, species whose item has at least one image.</param>
/// <param name="SpeciesWithUsablePhoto">Species left with at least one stored (republishable, creditable) photo.</param>
/// <param name="Skipped">Images not stored, by reason.</param>
public sealed record CommonsPhotoReport(
    int SpeciesLookedUp,
    int SpeciesOnWikidata,
    int SpeciesWithImage,
    int SpeciesWithUsablePhoto,
    int Inserted,
    int Updated,
    int Deleted,
    IReadOnlyDictionary<string, int> Skipped);

/// <summary>
/// Keeps <see cref="CommonsPhoto"/> in sync with Wikidata/Commons for every species: a few hundred names per SPARQL
/// query, 50 files per Commons query, so a full pass over ~15k species is a few hundred requests and can run weekly.
/// Only images that are open-licensed, unrestricted and credit an author are stored.
/// </summary>
public class CommonsPhotoIngestionService : ICommonsPhotoIngestionService
{
    public const int MaxPhotosPerSpecies = 3;
    /// <summary>Images of an item considered when looking for usable ones (items rarely have more than a few).</summary>
    public const int MaxCandidatesPerSpecies = 10;

    private static readonly HashSet<string> SupportedMimes = ["image/jpeg", "image/png"];

    private readonly IApplicationDbContext _db;
    private readonly ICommonsApiClient _client;
    private readonly ILogger<CommonsPhotoIngestionService> _logger;

    public int NamesPerQuery { get; set; } = 200;
    public int FilesPerQuery { get; set; } = 50;
    /// <summary>Pause between requests: Wikimedia asks bots to go one request at a time, gently.</summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(1);
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public CommonsPhotoIngestionService(
        IApplicationDbContext db, ICommonsApiClient client, ILogger<CommonsPhotoIngestionService> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    public async Task<CommonsPhotoReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var species = await _db.Species
            .Select(s => new { s.Id, s.ScientificName })
            .ToListAsync(cancellationToken);
        var idsByName = species
            .Where(s => !string.IsNullOrWhiteSpace(s.ScientificName))
            .GroupBy(s => s.ScientificName.Trim())
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());

        _logger.LogInformation("Looking up {Count} scientific names on Wikidata.", idsByName.Count);

        var totals = new Totals();
        foreach (var batch in idsByName.Keys.Chunk(NamesPerQuery))
        {
            IReadOnlyList<WikidataTaxonImages> taxa;
            try
            {
                taxa = await _client.FindTaxonImagesAsync(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Leave these species' stored photos untouched: a failed lookup is not "no image".
                _logger.LogError(ex, "Wikidata lookup failed for a batch of {Count} names; skipping it.", batch.Length);
                continue;
            }
            await Task.Delay(RequestDelay, cancellationToken);

            var infos = await FetchImageInfoAsync(taxa.SelectMany(t => t.FileTitles.Take(MaxCandidatesPerSpecies)), cancellationToken);
            if (infos is null) continue; // Commons failed: same rule, touch nothing

            var batchSpeciesIds = batch.SelectMany(name => idsByName[name]).ToList();
            var taxaByName = taxa.GroupBy(t => t.ScientificName).ToDictionary(g => g.Key, g => g.First());
            var desired = new List<CommonsPhoto>();

            foreach (var name in batch)
            {
                totals.LookedUp += idsByName[name].Count;
                if (!taxaByName.TryGetValue(name, out var taxon)) continue;
                totals.OnWikidata += idsByName[name].Count;
                if (taxon.FileTitles.Count > 0) totals.WithImage += idsByName[name].Count;

                // The first usable images, in the item's order: an unusable first image does not cost the species
                // its photo.
                var photos = taxon.FileTitles
                    .Take(MaxCandidatesPerSpecies)
                    .Select((title, rank) => ToPhoto(taxon, title, rank, infos, totals.Skipped))
                    .OfType<CommonsPhoto>()
                    .Take(MaxPhotosPerSpecies)
                    .ToList();

                foreach (var speciesId in idsByName[name])
                {
                    desired.AddRange(photos.Select(p => Copy(p, speciesId)));
                }
            }

            await ApplyAsync(batchSpeciesIds, desired, totals, cancellationToken);
        }

        var report = new CommonsPhotoReport(
            totals.LookedUp, totals.OnWikidata, totals.WithImage,
            await _db.CommonsPhotos.Select(p => p.SpeciesId).Distinct().CountAsync(cancellationToken),
            totals.Inserted, totals.Updated, totals.Deleted, totals.Skipped);

        _logger.LogInformation(
            "Commons sync: {LookedUp} species looked up, {OnWikidata} on Wikidata, {WithImage} with an image, " +
            "{Usable} with a usable photo. Inserted {Inserted}, updated {Updated}, deleted {Deleted}.",
            report.SpeciesLookedUp, report.SpeciesOnWikidata, report.SpeciesWithImage, report.SpeciesWithUsablePhoto,
            report.Inserted, report.Updated, report.Deleted);
        foreach (var (reason, count) in report.Skipped)
            _logger.LogInformation("Images skipped ({Reason}): {Count}", reason, count);

        return report;
    }

    private async Task<Dictionary<string, CommonsImageInfo>?> FetchImageInfoAsync(
        IEnumerable<string> titles, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CommonsImageInfo>(StringComparer.Ordinal);
        foreach (var chunk in titles.Select(NormalizeTitle).Distinct().Chunk(FilesPerQuery))
        {
            try
            {
                foreach (var info in await _client.GetImageInfoAsync(chunk, cancellationToken))
                    result[NormalizeTitle(info.FileTitle)] = info;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Commons imageinfo failed for {Count} files; skipping the batch.", chunk.Length);
                return null;
            }
            await Task.Delay(RequestDelay, cancellationToken);
        }
        return result;
    }

    private CommonsPhoto? ToPhoto(WikidataTaxonImages taxon, string title, int rank,
        IReadOnlyDictionary<string, CommonsImageInfo> infos, Dictionary<string, int> skipped)
    {
        string? reason = null;
        if (!infos.TryGetValue(NormalizeTitle(title), out var info)) reason = "missing on Commons";
        else if (!SupportedMimes.Contains(info.Mime)) reason = "not a photo";
        else if (!string.IsNullOrWhiteSpace(info.Restrictions)) reason = "restricted";
        else if (CommonsLicenses.ReuseOf(info.LicenseCode) != LicenseReuse.Open) reason = "license not open";
        else if (PlainText(info.ArtistHtml) is not { Length: > 0 }) reason = "no author to credit";

        if (reason is not null)
        {
            skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
            return null;
        }

        return new CommonsPhoto
        {
            WikidataId = taxon.WikidataId,
            FileTitle = NormalizeTitle(title),
            Rank = rank,
            ImageUrl = info!.ThumbnailUrl,
            Width = info.ThumbnailWidth,
            Height = info.ThumbnailHeight,
            DescriptionUrl = info.DescriptionUrl,
            Artist = Truncate(PlainText(info.ArtistHtml), CommonsPhoto.MaxArtistLength),
            LicenseCode = info.LicenseCode.Trim().ToLowerInvariant(),
            LicenseShortName = info.LicenseShortName,
            LicenseUrl = string.IsNullOrWhiteSpace(info.LicenseUrl) ? null : info.LicenseUrl,
            FetchedAt = UtcNow(),
        };
    }

    /// <summary>Makes the stored photos of <paramref name="speciesIds"/> exactly <paramref name="desired"/>.</summary>
    private async Task ApplyAsync(List<int> speciesIds, List<CommonsPhoto> desired, Totals totals,
        CancellationToken cancellationToken)
    {
        var existing = await _db.CommonsPhotos
            .Where(p => speciesIds.Contains(p.SpeciesId))
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(p => (p.SpeciesId, p.FileTitle));
        var wanted = desired.Select(p => (p.SpeciesId, p.FileTitle)).ToHashSet();

        foreach (var photo in desired)
        {
            if (byKey.TryGetValue((photo.SpeciesId, photo.FileTitle), out var current))
            {
                Overwrite(current, photo);
                totals.Updated++;
            }
            else
            {
                _db.CommonsPhotos.Add(photo);
                totals.Inserted++;
            }
        }

        foreach (var stale in existing.Where(p => !wanted.Contains((p.SpeciesId, p.FileTitle))))
        {
            _db.CommonsPhotos.Remove(stale);
            totals.Deleted++;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Commons titles use spaces, not underscores, and always carry the "File:" namespace.</summary>
    public static string NormalizeTitle(string title)
    {
        var t = title.Replace('_', ' ').Trim();
        return t.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ? "File:" + t[5..] : "File:" + t;
    }

    /// <summary>Commons' Artist field is HTML (links, spans, bdi); a credit line needs the text.</summary>
    public static string PlainText(string html)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html ?? string.Empty, "<[^>]+>", " "));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static CommonsPhoto Copy(CommonsPhoto p, int speciesId) => new()
    {
        SpeciesId = speciesId, WikidataId = p.WikidataId, FileTitle = p.FileTitle, Rank = p.Rank,
        ImageUrl = p.ImageUrl, Width = p.Width, Height = p.Height, DescriptionUrl = p.DescriptionUrl,
        Artist = p.Artist, LicenseCode = p.LicenseCode, LicenseShortName = p.LicenseShortName,
        LicenseUrl = p.LicenseUrl, FetchedAt = p.FetchedAt,
    };

    private static void Overwrite(CommonsPhoto target, CommonsPhoto source)
    {
        target.WikidataId = source.WikidataId;
        target.Rank = source.Rank;
        target.ImageUrl = source.ImageUrl;
        target.Width = source.Width;
        target.Height = source.Height;
        target.DescriptionUrl = source.DescriptionUrl;
        target.Artist = source.Artist;
        target.LicenseCode = source.LicenseCode;
        target.LicenseShortName = source.LicenseShortName;
        target.LicenseUrl = source.LicenseUrl;
        target.FetchedAt = source.FetchedAt;
    }

    private sealed class Totals
    {
        public int LookedUp, OnWikidata, WithImage, Inserted, Updated, Deleted;
        public Dictionary<string, int> Skipped { get; } = new();
    }
}
