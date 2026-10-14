using System.Net;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Flickr.Synch.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Flickr;

/// <summary>
/// The license decides whether Birdetector may republish a photo on social media, so the failure that matters is a
/// photo read as more open than it is: unknown ids, malformed values and photos Flickr no longer serves must all
/// stay non-republishable.
/// </summary>
public class FlickrLicenseTests
{
    [Theory]
    [InlineData(4, LicenseReuse.Open)]          // CC BY 2.0
    [InlineData(11, LicenseReuse.Open)]         // CC BY 4.0
    [InlineData(12, LicenseReuse.Open)]         // CC BY-SA 4.0
    [InlineData(9, LicenseReuse.Open)]          // CC0
    [InlineData(10, LicenseReuse.Open)]         // Public Domain Mark
    [InlineData(2, LicenseReuse.NonCommercial)] // CC BY-NC 2.0
    [InlineData(15, LicenseReuse.NonCommercial)]// CC BY-NC-SA 4.0
    [InlineData(6, LicenseReuse.NoDerivatives)] // CC BY-ND 2.0
    [InlineData(16, LicenseReuse.NoDerivatives)]// CC BY-NC-ND 4.0
    [InlineData(0, LicenseReuse.Reserved)]      // All Rights Reserved
    public void ClassifiesEveryDocumentedFlickrLicense(int id, LicenseReuse expected) =>
        Assert.Equal(expected, FlickrLicenses.ReuseOf(id));

    [Fact]
    public void AnUnknownOrMissingLicenseIsNeverRepublishable()
    {
        Assert.Equal(LicenseReuse.Reserved, FlickrLicenses.ReuseOf(null));
        Assert.Equal(LicenseReuse.Reserved, FlickrLicenses.ReuseOf(99));
    }

    [Fact]
    public void CoversAllSeventeenLicensesFlickrDocuments() =>
        Assert.Equal(Enumerable.Range(0, 17), FlickrLicenses.All.Select(l => l.Id).Order());

    [Theory]
    [InlineData("4", 4)]
    [InlineData("0", 0)]
    [InlineData("", null)]
    [InlineData("cc-by", null)]
    [InlineData("-1", null)]
    [InlineData(null, null)]
    public void ParsesFlickrsNumericLicenseString(string? raw, int? expected) =>
        Assert.Equal(expected, FlickrLicenses.Parse(raw));

    [Theory]
    [InlineData("cc-by", LicenseReuse.Open)]
    [InlineData("CC0", LicenseReuse.Open)]
    [InlineData("cc-by-nc", LicenseReuse.NonCommercial)]
    [InlineData("cc-by-nd", LicenseReuse.NoDerivatives)]
    [InlineData(null, LicenseReuse.Reserved)]
    public void ClassifiesINaturalistLicenseCodes(string? code, LicenseReuse expected) =>
        Assert.Equal(expected, INatLicenses.ReuseOf(code));

    [Fact]
    public async Task TheSyncStoresTheLicenseOfNewPhotos()
    {
        using var db = TestDatabase.Create();
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Pool(pages: 1, (101, "4"), (102, "0")))
            .AlwaysRespondWith(HttpStatusCode.OK, Pool(pages: 1));

        var sync = new FlickrSynchService(db.NewContext(), new StubHttpClientFactory(http), Config(),
            NullLogger<FlickrSynchService>.Instance);
        await sync.SynchronizeAsync();

        Assert.Contains("license", http.Requests[0].RequestUri!.Query);
        using var assertions = db.NewContext();
        var licenses = await assertions.FlickrPhotos.OrderBy(p => p.PhotoId).Select(p => p.LicenseId).ToListAsync();
        Assert.Equal([4, 0], licenses);
    }

    [Fact]
    public async Task TheBackfillFillsLicensesFromTheGroupPool()
    {
        using var db = TestDatabase.Create();
        await SeedPhotosAsync(db, 101, 102, 103);
        // 999 is in the pool but not in the database: it must not be created.
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Pool(pages: 1, (101, "11"), (102, "2"), (999, "4")));

        var report = await Backfill(db, http).RunAsync(photoInfoLimit: 0);

        Assert.Equal(2, report.PoolUpdates);
        using var assertions = db.NewContext();
        var licenses = await assertions.FlickrPhotos.OrderBy(p => p.PhotoId).Select(p => p.LicenseId).ToListAsync();
        Assert.Equal([11, 2, null], licenses);
    }

    [Fact]
    public async Task TheBackfillPagesUntilTheLastPoolPage()
    {
        using var db = TestDatabase.Create();
        await SeedPhotosAsync(db, 101, 102);
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Pool(pages: 2, (101, "4")))
            .RespondWith(HttpStatusCode.OK, Pool(pages: 2, (102, "5")));

        var report = await Backfill(db, http).RunAsync(photoInfoLimit: 0);

        Assert.Equal(2, report.PoolUpdates);
        Assert.Equal(2, http.RequestCount);
    }

    [Fact]
    public async Task GetInfoOnlyAsksForSpeciesLinkedPhotosAndSkipsDeletedOnes()
    {
        using var db = TestDatabase.Create();
        await SeedPhotosAsync(db, 101, 102, 103);
        await LinkAsync(db, ("rambto1", [101, 102]));
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Pool(pages: 1))                                    // pool: nothing new
            .RespondWith(HttpStatusCode.OK, """{"photo":{"id":"101","license":"12"},"stat":"ok"}""")
            .RespondWith(HttpStatusCode.OK, """{"stat":"fail","code":1,"message":"Photo not found"}""");

        var report = await Backfill(db, http).RunAsync(photoInfoLimit: 10);

        Assert.Equal(1, report.PhotoInfoUpdates);
        Assert.Equal(3, http.RequestCount); // 103 is not linked to a species: never asked about
        using var assertions = db.NewContext();
        var licenses = await assertions.FlickrPhotos.OrderBy(p => p.PhotoId).Select(p => p.LicenseId).ToListAsync();
        Assert.Equal([12, null, null], licenses);
    }

    [Fact]
    public async Task TheReportCountsSpeciesPerLicensePolicyAcrossFlickrAndINat()
    {
        using var db = TestDatabase.Create();
        await SeedPhotosAsync(db, (101, 11), (102, 0), (103, null));
        await LinkAsync(db, ("open", [101]), ("reserved", [102]), ("unknown", [103]));
        await LinkINatAsync(db, ("inatnc", "cc-by-nc"), ("inatopen", "cc0"));
        var http = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.OK, Pool(pages: 1));

        var report = await Backfill(db, http).RunAsync(photoInfoLimit: 0);

        Assert.Equal(5, report.SpeciesWithAnyPhoto);
        Assert.Equal(2, report.SpeciesWithOpenPhoto);                // open (Flickr) + inatopen
        Assert.Equal(3, report.SpeciesWithOpenOrNonCommercialPhoto); // + inatnc
        Assert.Equal(1, report.LinkedPhotosByReuse[LicenseReuse.Open]);
        Assert.Equal(1, report.LinkedPhotosByReuse[LicenseReuse.Reserved]);
        Assert.Equal(1, report.LinkedPhotosUnknown);
    }

    private static FlickrLicenseBackfill Backfill(TestDatabase db, StubHttpMessageHandler http) =>
        new(db.NewContext(), new StubHttpClientFactory(http), Config(), NullLogger<FlickrLicenseBackfill>.Instance)
        {
            PageDelay = TimeSpan.Zero,
            PhotoInfoDelay = TimeSpan.Zero,
        };

    private static Microsoft.Extensions.Configuration.IConfiguration Config() => TestConfiguration.With(
        ("FlickrApi:ApiKey", "test-key"), ("FlickrApi:PageSize", "500"), ("FlickrApi:Groups:0", "1988039@N24"));

    private static string Pool(int pages, params (long Id, string License)[] photos)
    {
        var items = string.Join(",", photos.Select(p =>
            $$"""{"id":"{{p.Id}}","owner":"1@N00","title":"t","ownername":"o","tags":"","license":"{{p.License}}"}"""));
        return $$"""{"photos":{"page":1,"pages":{{pages}},"perpage":500,"photo":[{{items}}]},"stat":"ok"}""";
    }

    private static Task SeedPhotosAsync(TestDatabase db, params long[] photoIds) =>
        SeedPhotosAsync(db, photoIds.Select(id => (id, (int?)null)).ToArray());

    private static async Task SeedPhotosAsync(TestDatabase db, params (long PhotoId, int? LicenseId)[] photos)
    {
        using var context = db.NewContext();
        foreach (var (photoId, licenseId) in photos)
        {
            context.FlickrPhotos.Add(new FlickrPhoto { PhotoId = photoId, LicenseId = licenseId });
        }
        await context.SaveChangesAsync();
    }

    private static async Task LinkAsync(TestDatabase db, params (string SpeciesCode, long[] PhotoIds)[] links)
    {
        using var context = db.NewContext();
        foreach (var (code, photoIds) in links)
        {
            var species = new Species { SpeciesCode = code, ScientificName = code };
            context.Species.Add(species);
            foreach (var photo in context.FlickrPhotos.Where(p => photoIds.Contains(p.PhotoId)))
            {
                context.SpeciesPhotos.Add(new SpeciesPhoto { Species = species, FlickrPhoto = photo });
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task LinkINatAsync(TestDatabase db, params (string SpeciesCode, string License)[] links)
    {
        using var context = db.NewContext();
        var photoId = 1L;
        foreach (var (code, license) in links)
        {
            context.SpeciesINatPhotos.Add(new SpeciesINatPhoto
            {
                Species = new Species { SpeciesCode = code, ScientificName = code },
                INatPhoto = new INatPhoto { PhotoId = photoId++, LicenseCode = license },
            });
        }
        await context.SaveChangesAsync();
    }
}
