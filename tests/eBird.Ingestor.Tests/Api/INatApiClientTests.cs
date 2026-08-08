using System.Net;
using eBird.Ingestor.Infrastructure.Services;
using eBird.Ingestor.Tests.TestSupport;
using Xunit;

namespace eBird.Ingestor.Tests.Api;

/// <summary>
/// iNaturalist returns everything anyone has ever photographed, so the client's guard filters —
/// birds only, geolocated, dated — are what stop a sloth from being written into a bird database.
/// Every test here pays the client's built-in ~1.1s politeness delay.
/// </summary>
public class INatApiClientTests
{
    [Fact]
    public async Task KeepsOnlyGeolocatedDatedBirdObservations()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("inat-observations.json"));

        var observations = (await ClientFor(handler).GetRecentObservationsAsync(6924, daysAgo: 180)).ToList();

        // The recorded page also contains a mammal, and a macaw with neither coordinates nor a date.
        Assert.Equal(
            new[] { "Ramphastos sulfuratus", "Pharomachrus mocinno" },
            observations.Select(o => o.Taxon.Name).ToArray());

        var toucan = observations[0];
        Assert.Equal(255000001L, toucan.Id);
        Assert.Equal("2026-04-21", toucan.ObservedOn);
        Assert.Equal(4, toucan.NumIdentificationAgreements);
        Assert.Equal(new[] { -84.0064, 10.4306 }, toucan.Geojson!.Coordinates);
        Assert.Equal("Ramphastidae", toucan.Identifications![0].Taxon.Ancestors!.Single(a => a.Rank == "family").Name);
    }

    [Fact]
    public async Task AsksForResearchGradeBirdsOnTheRequestedPlaceAndPage()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, "{\"results\":[]}");

        await ClientFor(handler).GetRecentObservationsAsync(6924, daysAgo: 180, page: 3);

        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("place_id=6924", query);
        Assert.Contains("page=3", query);
        Assert.Contains("per_page=200", query);
        Assert.Contains("iconic_taxa=Aves", query);
        Assert.Contains("quality_grade=research", query);
    }

    [Fact]
    public async Task ReturnsNothingWhenTheResponseHasNoResultsProperty()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, "{}");

        Assert.Empty(await ClientFor(handler).GetRecentObservationsAsync(6924, daysAgo: 180));
    }

    [Fact]
    public async Task ReturnsTheLocalizedCommonNameForATaxon()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("inat-taxon-es.json"));

        var name = await ClientFor(handler).GetTaxonLocalizedNameAsync(18762, "es");

        Assert.Equal("Tucán Pico Iris", name);
        Assert.Contains("/taxa/18762?locale=es", Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task ReturnsNullRatherThanFailingTheRunWhenTheTaxonLookupIsRejected()
    {
        // A missing translation must never take down an ingestion run, so this path swallows
        // everything. 400 is not transient, so it is not retried either.
        var handler = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.BadRequest, "");

        Assert.Null(await ClientFor(handler).GetTaxonLocalizedNameAsync(18762, "de"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task BacksOffAndRetriesWhenINaturalistRateLimitsTheRun()
    {
        // iNat allows 60 requests a minute and answers 429 past that. Unlike the other clients,
        // this one treats 429 as retryable — losing a whole country to a momentary throttle would
        // mean re-fetching it from scratch on the next timer.
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.TooManyRequests, "")
            .RespondWith(HttpStatusCode.OK, Fixture.Load("inat-observations.json"));

        var observations = await ClientFor(handler).GetRecentObservationsAsync(6924, daysAgo: 180);

        Assert.Equal(2, observations.Count());
        Assert.Equal(2, handler.RequestCount);
    }

    private static INatApiClient ClientFor(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), TestConfiguration.Empty());
}
