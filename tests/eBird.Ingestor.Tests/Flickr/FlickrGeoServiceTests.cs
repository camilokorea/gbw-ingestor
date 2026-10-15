using System.Net;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Flickr.Geo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Flickr;

/// <summary>
/// The geo worker shares one Flickr key with the other workers. It used to re-ask about every photo without a
/// location on every run (~320k calls, more than half answered 429, nothing saved), so the properties that matter are:
/// a definitive "no location" is remembered, throttling makes it back off and eventually stop, and a run is bounded.
/// </summary>
public class FlickrGeoServiceTests
{
    private const string Located = """
        {"photo":{"id":"101","location":{"latitude":"9.9281","longitude":"-84.0907","accuracy":"16","context":"2",
         "locality":{"_content":"San José","place_id":"p1","woeid":"1"},"country":{"_content":"Costa Rica","place_id":"p2","woeid":"23424791"},
         "place_id":"p1","woeid":"1"}},"stat":"ok"}
        """;
    private const string NoLocation = """{"stat":"fail","code":2,"message":"Photo has no location information."}""";
    private const string NotFound = """{"stat":"fail","code":1,"message":"Photo not found."}""";

    [Fact]
    public async Task A_location_is_stored_and_the_photo_marked_as_checked()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101);
        var http = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, Located);

        var report = await Service(db, http).GeolocatePhotosAsync();

        Assert.Equal(1, report.Located);
        using var assertions = db.NewContext();
        var location = await assertions.FlickrPhotoLocations.SingleAsync();
        Assert.Equal((9.9281, -84.0907, "Costa Rica"), (location.Latitude!.Value, location.Longitude!.Value, location.CountryName));
        Assert.NotNull((await assertions.FlickrPhotos.SingleAsync()).GeoCheckedAt);
    }

    [Fact]
    public async Task Photos_without_a_location_are_remembered_and_never_asked_about_again()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101, 102);
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, NoLocation)
            .RespondWith(HttpStatusCode.OK, NotFound);

        var first = await Service(db, http).GeolocatePhotosAsync();
        var requestsAfterFirstRun = http.RequestCount;
        var second = await Service(db, http).GeolocatePhotosAsync();

        Assert.Equal(2, first.WithoutLocation);
        Assert.Equal(2, requestsAfterFirstRun);
        Assert.Equal(2, http.RequestCount); // the second run asked nothing
        Assert.Equal(0, second.WithoutLocation + second.Located);
    }

    [Fact]
    public async Task A_429_pauses_and_the_run_goes_on_with_the_next_photo()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101, 102);
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.TooManyRequests, "")
            .RespondWith(HttpStatusCode.OK, NoLocation);

        var report = await Service(db, http).GeolocatePhotosAsync();

        Assert.Equal((1, 1, false), (report.Skipped, report.WithoutLocation, report.StoppedByThrottling));
        using var assertions = db.NewContext();
        // The throttled photo is left unchecked for a later run.
        Assert.Equal(1, await assertions.FlickrPhotos.CountAsync(p => p.GeoCheckedAt == null));
    }

    [Fact]
    public async Task Persistent_throttling_stops_the_run_instead_of_burning_the_key()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, Enumerable.Range(1, 20).Select(i => (long)i).ToArray());
        var http = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.TooManyRequests, "");

        var report = await Service(db, http).GeolocatePhotosAsync();

        Assert.True(report.StoppedByThrottling);
        Assert.Equal(5, http.RequestCount);
    }

    [Fact]
    public async Task Transient_errors_leave_the_photo_for_a_later_run()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101);
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.InternalServerError, "")
            .RespondWith(HttpStatusCode.OK, """{"stat":"fail","code":105,"message":"Service currently unavailable"}""");

        var first = await Service(db, http).GeolocatePhotosAsync();
        var second = await Service(db, http).GeolocatePhotosAsync();

        Assert.Equal(1, first.Skipped);
        Assert.Equal(1, second.Skipped);
        using var assertions = db.NewContext();
        Assert.Null((await assertions.FlickrPhotos.SingleAsync()).GeoCheckedAt);
    }

    [Fact]
    public async Task An_invalid_api_key_fails_the_run_loudly()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101);
        var http = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, """{"stat":"fail","code":100,"message":"Invalid API Key (Key has invalid format)"}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, http).GeolocatePhotosAsync());
    }

    [Fact]
    public async Task A_run_is_bounded_and_species_linked_photos_come_first()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db, 101, 102, 103);
        await LinkToSpeciesAsync(db, 101);
        var http = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.OK, NoLocation);
        var service = Service(db, http);
        service.MaxPerRun = 1;

        await service.GeolocatePhotosAsync();

        Assert.Single(http.Requests);
        Assert.Contains("photo_id=101", http.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public void The_pace_comes_from_configuration()
    {
        using var db = TestDatabase.Create();
        var service = new FlickrGeoService(db.NewContext(), new StubHttpClientFactory(new StubHttpMessageHandler()),
            TestConfiguration.With(("FlickrApi:ApiKey", "k"), ("FlickrGeo:RequestsPerHour", "3600"), ("FlickrGeo:MaxPerRun", "50")),
            NullLogger<FlickrGeoService>.Instance);

        Assert.Equal(TimeSpan.FromSeconds(1), service.RequestInterval);
        Assert.Equal(50, service.MaxPerRun);
    }

    private static FlickrGeoService Service(TestDatabase db, StubHttpMessageHandler http) =>
        new(db.NewContext(), new StubHttpClientFactory(http), TestConfiguration.With(("FlickrApi:ApiKey", "test-key")),
            NullLogger<FlickrGeoService>.Instance)
        {
            RequestInterval = TimeSpan.Zero,
            ThrottleBackoff = TimeSpan.Zero,
        };

    private static async Task SeedAsync(TestDatabase db, params long[] photoIds)
    {
        using var context = db.NewContext();
        foreach (var id in photoIds) context.FlickrPhotos.Add(new FlickrPhoto { PhotoId = id });
        await context.SaveChangesAsync();
    }

    private static async Task LinkToSpeciesAsync(TestDatabase db, long photoId)
    {
        using var context = db.NewContext();
        var species = new Species { SpeciesCode = "sp1", ScientificName = "Avis una" };
        context.SpeciesPhotos.Add(new SpeciesPhoto { Species = species, FlickrPhoto = await context.FlickrPhotos.SingleAsync(p => p.PhotoId == photoId) });
        await context.SaveChangesAsync();
    }
}
