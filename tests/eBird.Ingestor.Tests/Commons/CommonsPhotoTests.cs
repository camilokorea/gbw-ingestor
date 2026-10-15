using System.Net;
using System.Text.Json;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Services;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Commons;

/// <summary>
/// Commons photos end up on Birdetector's social accounts with a credit line, so what matters is that only images we
/// may republish get stored, that the stored credit is readable text, and that a failed lookup never wipes photos.
/// Parsing is checked against responses recorded from Wikidata and Commons (2026-10-15).
/// </summary>
public class CommonsPhotoTests
{
    [Fact]
    public void ParsesTheWikidataResponseIntoTaxaWithTheirImages()
    {
        var taxa = CommonsApiClient.ParseSparql(Json("wikidata-taxon-images.json"));

        Assert.Equal(["Passer domesticus", "Ramphastos sulfuratus", "Turdus grayi"], taxa.Select(t => t.ScientificName).Order());
        var toucan = taxa.Single(t => t.ScientificName == "Ramphastos sulfuratus");
        Assert.Equal("Q47501", toucan.WikidataId);
        Assert.Equal(["File:Keel billed toucan costa rica (cropped).jpg"], toucan.FileTitles);
        Assert.Equal(2, taxa.Single(t => t.ScientificName == "Passer domesticus").FileTitles.Count);
    }

    [Fact]
    public void ParsesCommonsImageInfoAndSkipsMissingFiles()
    {
        var infos = CommonsApiClient.ParseImageInfo(Json("commons-imageinfo.json"));

        Assert.Equal(3, infos.Count);
        var toucan = infos.Single(i => i.FileTitle == "File:Keel billed toucan costa rica (cropped).jpg");
        Assert.Equal("cc-by-sa-3.0", toucan.LicenseCode);
        Assert.Equal("CC BY-SA 3.0", toucan.LicenseShortName);
        Assert.Equal(1080, toucan.ThumbnailWidth);
        Assert.StartsWith("https://", toucan.ThumbnailUrl);
        Assert.DoesNotContain("?", toucan.ThumbnailUrl); // utm tracking parameters stripped
        Assert.Contains("Ttschleuder", toucan.ArtistHtml);
    }

    [Theory]
    [InlineData("http://commons.wikimedia.org/wiki/Special:FilePath/Keel%20billed%20toucan%20costa%20rica%20%28cropped%29.jpg",
        "File:Keel billed toucan costa rica (cropped).jpg")]
    [InlineData("http://commons.wikimedia.org/wiki/Special:FilePath/House_sparrow.jpg", "File:House sparrow.jpg")]
    public void TurnsWikidataFilePathUrlsIntoCommonsTitles(string url, string title) =>
        Assert.Equal(title, CommonsApiClient.FileTitleFromFilePath(url));

    [Fact]
    public void TheArtistIsStoredAsPlainText() =>
        Assert.Equal("Charles J. Sharp", CommonsPhotoIngestionService.PlainText(
            """<bdi><a href="https://www.wikidata.org/wiki/Q54800218" class="extiw"><span title="Scottish wildlife photographer">Charles J. Sharp</span></a></bdi>"""));

    [Theory]
    [InlineData("cc-by-sa-4.0", LicenseReuse.Open)]
    [InlineData("cc-by-2.0", LicenseReuse.Open)]
    [InlineData("cc0", LicenseReuse.Open)]
    [InlineData("pd", LicenseReuse.Open)]
    [InlineData("cc-by-nc-2.0", LicenseReuse.NonCommercial)]
    [InlineData("cc-by-nd-4.0", LicenseReuse.NoDerivatives)]
    [InlineData("gfdl", LicenseReuse.Reserved)]
    [InlineData("", LicenseReuse.Reserved)]
    [InlineData(null, LicenseReuse.Reserved)]
    public void ClassifiesCommonsLicenses(string? code, LicenseReuse expected) =>
        Assert.Equal(expected, CommonsLicenses.ReuseOf(code));

    [Fact]
    public async Task TheClientRetriesWhenWikidataThrottles()
    {
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.TooManyRequests, "")
            .RespondWith(HttpStatusCode.OK, Fixture.Load("wikidata-taxon-images.json"));
        var client = new CommonsApiClient(new StubHttpClientFactory(http), NullLogger<CommonsApiClient>.Instance)
        {
            RetryBaseDelay = TimeSpan.Zero,
        };

        var taxa = await client.FindTaxonImagesAsync(["Ramphastos sulfuratus"]);

        Assert.Equal(2, http.RequestCount);
        Assert.Equal(HttpMethod.Post, http.Requests[0].Method);
        Assert.Equal(3, taxa.Count);
    }

    [Fact]
    public async Task StoresOnlyOpenUnrestrictedCreditedPhotos()
    {
        using var db = TestDatabase.Create();
        var ids = await SeedSpeciesAsync(db, "Ramphastos sulfuratus", "Turdus grayi");
        var client = new FakeCommons()
            .Taxon("Ramphastos sulfuratus", "Q47501", "File:open.jpg", "File:gfdl.jpg", "File:restricted.jpg",
                "File:anonymous.jpg", "File:gone.jpg")
            .Taxon("Turdus grayi", "Q551736")
            .Image("File:open.jpg", "cc-by-sa-4.0", "<a href=\"x\">Ana Pérez</a>")
            .Image("File:gfdl.jpg", "gfdl", "Ana")
            .Image("File:restricted.jpg", "cc-by-4.0", "Ana", restrictions: "trademarked")
            .Image("File:anonymous.jpg", "cc0", "  ");

        var report = await Service(db, client).SyncAsync();

        using var assertions = db.NewContext();
        var photo = Assert.Single(await assertions.CommonsPhotos.ToListAsync());
        Assert.Equal(ids["Ramphastos sulfuratus"], photo.SpeciesId);
        Assert.Equal("File:open.jpg", photo.FileTitle);
        Assert.Equal("Ana Pérez", photo.Artist);
        Assert.Equal("CC BY-SA 4.0", photo.LicenseShortName);
        Assert.Equal(2, report.SpeciesOnWikidata);
        Assert.Equal(1, report.SpeciesWithImage);
        Assert.Equal(1, report.SpeciesWithUsablePhoto);
        Assert.Equal(1, report.Skipped["license not open"]);
        Assert.Equal(1, report.Skipped["restricted"]);
        Assert.Equal(1, report.Skipped["no author to credit"]);
        Assert.Equal(1, report.Skipped["missing on Commons"]);
    }

    [Fact]
    public async Task KeepsTheFirstThreeUsableImagesEvenWhenEarlierOnesAreUnusable()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");
        var client = new FakeCommons()
            .Taxon("Ramphastos sulfuratus", "Q47501", "File:gfdl.jpg", "File:1.jpg", "File:2.jpg", "File:3.jpg", "File:4.jpg")
            .Image("File:gfdl.jpg", "gfdl", "Ana")
            .Image("File:1.jpg", "cc-by-4.0", "Ana").Image("File:2.jpg", "cc0", "Ana")
            .Image("File:3.jpg", "pd", "Ana").Image("File:4.jpg", "cc-by-4.0", "Ana");

        await Service(db, client).SyncAsync();

        using var assertions = db.NewContext();
        var photos = await assertions.CommonsPhotos.OrderBy(p => p.Rank).ToListAsync();
        Assert.Equal(["File:1.jpg", "File:2.jpg", "File:3.jpg"], photos.Select(p => p.FileTitle));
        Assert.Equal([1, 2, 3], photos.Select(p => p.Rank));
    }

    [Fact]
    public async Task TitlesDifferingOnlyInCaseAreStoredOnce()
    {
        // Seen in production: an item listing "Golden-Olive Woodpecker.jpg" and "Golden-olive Woodpecker.jpg". Distinct
        // to C#, equal to SQL Server's case-insensitive unique index.
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Colaptes rubiginosus");
        var client = new FakeCommons()
            .Taxon("Colaptes rubiginosus", "Q1", "File:Golden-Olive Woodpecker.jpg", "File:Golden-olive Woodpecker.jpg")
            .Image("File:Golden-Olive Woodpecker.jpg", "cc-by-4.0", "Ana")
            .Image("File:Golden-olive Woodpecker.jpg", "cc-by-4.0", "Ana");

        await Service(db, client).SyncAsync();
        await Service(db, client).SyncAsync();

        using var assertions = db.NewContext();
        Assert.Single(await assertions.CommonsPhotos.ToListAsync());
    }

    [Fact]
    public async Task ASecondRunUpdatesChangedPhotosAndRemovesOnesNoLongerOnWikidata()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");
        await Service(db, new FakeCommons()
            .Taxon("Ramphastos sulfuratus", "Q47501", "File:a.jpg", "File:b.jpg")
            .Image("File:a.jpg", "cc-by-4.0", "Ana")
            .Image("File:b.jpg", "cc-by-4.0", "Beto")).SyncAsync();

        var report = await Service(db, new FakeCommons()
            .Taxon("Ramphastos sulfuratus", "Q47501", "File:a.jpg")
            .Image("File:a.jpg", "cc0", "Ana María")).SyncAsync();

        using var assertions = db.NewContext();
        var photo = Assert.Single(await assertions.CommonsPhotos.ToListAsync());
        Assert.Equal(("File:a.jpg", "Ana María", "cc0"), (photo.FileTitle, photo.Artist, photo.LicenseCode));
        Assert.Equal((0, 1, 1), (report.Inserted, report.Updated, report.Deleted));
    }

    [Fact]
    public async Task AFailedLookupNeverDeletesStoredPhotos()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");
        await Service(db, new FakeCommons()
            .Taxon("Ramphastos sulfuratus", "Q47501", "File:a.jpg")
            .Image("File:a.jpg", "cc-by-4.0", "Ana")).SyncAsync();

        var report = await Service(db, new FakeCommons { FailLookups = true }).SyncAsync();

        using var assertions = db.NewContext();
        Assert.Single(await assertions.CommonsPhotos.ToListAsync());
        Assert.Equal(0, report.SpeciesLookedUp);
        Assert.Equal(0, report.Deleted);
    }

    [Fact]
    public async Task NamesAreLookedUpInBatches()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "A a", "B b", "C c", "D d", "E e");
        var client = new FakeCommons();
        var service = Service(db, client);
        service.NamesPerQuery = 2;

        await service.SyncAsync();

        Assert.Equal([2, 2, 1], client.LookupBatchSizes);
    }

    private static CommonsPhotoIngestionService Service(TestDatabase db, ICommonsApiClient client) =>
        new(db.NewContext(), client, NullLogger<CommonsPhotoIngestionService>.Instance) { RequestDelay = TimeSpan.Zero };

    private static async Task<Dictionary<string, int>> SeedSpeciesAsync(TestDatabase db, params string[] names)
    {
        using var context = db.NewContext();
        var species = names.Select((n, i) => new Species { SpeciesCode = $"sp{i}", ScientificName = n }).ToList();
        context.Species.AddRange(species);
        await context.SaveChangesAsync();
        return species.ToDictionary(s => s.ScientificName, s => s.Id);
    }

    private static JsonElement Json(string fixture) => JsonDocument.Parse(Fixture.Load(fixture)).RootElement;

    private sealed class FakeCommons : ICommonsApiClient
    {
        private readonly List<WikidataTaxonImages> _taxa = [];
        private readonly Dictionary<string, CommonsImageInfo> _images = [];

        public bool FailLookups { get; init; }
        public List<int> LookupBatchSizes { get; } = [];

        public FakeCommons Taxon(string name, string qid, params string[] files)
        {
            _taxa.Add(new WikidataTaxonImages(name, qid, files));
            return this;
        }

        public FakeCommons Image(string title, string license, string artistHtml, string restrictions = "")
        {
            _images[title] = new CommonsImageInfo(title, $"https://upload.wikimedia.org/{title}", 1080, 720,
                $"https://commons.wikimedia.org/wiki/{title}", "image/jpeg", artistHtml, license,
                license.ToUpperInvariant().Replace("-", " ").Replace("CC BY SA", "CC BY-SA"), null, restrictions);
            return this;
        }

        public Task<IReadOnlyList<WikidataTaxonImages>> FindTaxonImagesAsync(
            IReadOnlyCollection<string> scientificNames, CancellationToken cancellationToken = default)
        {
            LookupBatchSizes.Add(scientificNames.Count);
            if (FailLookups) throw new HttpRequestException("query.wikidata.org answered HTTP 500");
            return Task.FromResult<IReadOnlyList<WikidataTaxonImages>>(
                _taxa.Where(t => scientificNames.Contains(t.ScientificName)).ToList());
        }

        public Task<IReadOnlyList<CommonsImageInfo>> GetImageInfoAsync(
            IReadOnlyCollection<string> fileTitles, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommonsImageInfo>>(
                fileTitles.Where(_images.ContainsKey).Select(t => _images[t]).ToList());
    }
}
