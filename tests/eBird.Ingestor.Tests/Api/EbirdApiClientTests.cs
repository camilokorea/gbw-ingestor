using System.Net;
using eBird.Ingestor.Infrastructure.Services;
using eBird.Ingestor.Tests.TestSupport;
using Xunit;

namespace eBird.Ingestor.Tests.Api;

/// <summary>
/// eBird is the ingestor's only source of observations and it is not always up. What matters is
/// what the client does with a bad response: recover quietly where it can, and throw where it
/// cannot so the region is marked Failed and retried on the next timer rather than silently
/// skipped.
/// </summary>
public class EbirdApiClientTests
{
    [Fact]
    public async Task RefusesToCallEBirdWithoutAnApiKey()
    {
        var handler = new StubHttpMessageHandler();
        var client = new EbirdApiClient(new StubHttpClientFactory(handler), TestConfiguration.Empty());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetRecentObservationsAsync("CR"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task RequestsTheRegionEndpointWithTheApiTokenHeader()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("ebird-recent-observations.json"));

        await ClientFor(handler).GetRecentObservationsAsync("CR");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.ebird.org/v2/data/obs/CR/recent", request.RequestUri!.ToString());
        Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("X-eBirdApiToken")));
    }

    [Fact]
    public async Task ParsesARecordedRecentObservationsResponse()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("ebird-recent-observations.json"));

        var observations = (await ClientFor(handler).GetRecentObservationsAsync("CR")).ToList();

        Assert.Equal(2, observations.Count);

        var toucan = observations[0];
        Assert.Equal("rambto1", toucan.SpeciesCode);
        Assert.Equal("Ramphastos sulfuratus", toucan.SciName);
        Assert.Equal("Keel-billed Toucan", toucan.ComName);
        Assert.Equal("L1234567", toucan.LocId);
        Assert.Equal("2026-04-21 07:15", toucan.ObsDt);
        Assert.Equal(3, toucan.HowMany);
        Assert.Equal(10.4306, toucan.Lat);
        Assert.Equal(-84.0064, toucan.Lng);

        // eBird omits howMany entirely when the observer recorded "X" (present, uncounted).
        // It lands as 0, which is what gets stored — worth knowing before reading the totals.
        Assert.Equal(0, observations[1].HowMany);
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenTheRegionHasNoObservations()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, "[]");

        Assert.Empty(await ClientFor(handler).GetRecentObservationsAsync("AQ"));
    }

    [Fact]
    public async Task ParsesLocationDetails()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("ebird-location-info.json"));

        var details = await ClientFor(handler).GetLocationDetailsAsync("L1234567");

        Assert.Equal("CR-H", details.StateCode);
        Assert.Equal("Heredia", details.StateName);
    }

    [Fact]
    public async Task ParsesTheLocalizedCommonNameOutOfTheTaxonomyResponse()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("ebird-taxonomy-es.json"));

        var name = await ClientFor(handler).GetSpeciesLocalizedNameAsync("rambto1", "es");

        Assert.Equal("Tucán Pico Iris", name);
        Assert.Contains("locale=es", Assert.Single(handler.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task ReturnsNullWhenTheTaxonomyResponseHasNoEntries()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, "[]");

        Assert.Null(await ClientFor(handler).GetSpeciesLocalizedNameAsync("rambto1", "pt"));
    }

    [Fact]
    public async Task DoesNotRetryARateLimitedResponseAndSurfacesItToTheCaller()
    {
        // 429 is not in Polly's transient set here, so it goes straight to the caller. That is
        // survivable — the ingestion loop marks the region Failed and the timer retries it — but
        // it means a throttled run makes no progress at all for that region.
        var handler = new StubHttpMessageHandler()
            .AlwaysRespondWith(HttpStatusCode.TooManyRequests, "rate limited");

        await Assert.ThrowsAsync<HttpRequestException>(() => ClientFor(handler).GetRecentObservationsAsync("CR"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RecoversFromATransientServerErrorByRetrying()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.ServiceUnavailable, "eBird is briefly unavailable")
            .RespondWith(HttpStatusCode.OK, Fixture.Load("ebird-recent-observations.json"));

        var observations = await ClientFor(handler).GetRecentObservationsAsync("CR");

        Assert.Equal(2, observations.Count());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Documents_AMissingTranslationCostsFourRequestsAndFourteenSecondsOfBackoff()
    {
        // The retry policy treats 404 as transient, so every species eBird has no translation for
        // burns four requests and ~14s of backoff per locale, on every new species. It does return
        // null in the end, which is why nothing has ever surfaced this.
        var handler = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.NotFound, "");

        var name = await ClientFor(handler).GetSpeciesLocalizedNameAsync("rambto1", "fr");

        Assert.Null(name);
        Assert.Equal(4, handler.RequestCount);
    }

    private static EbirdApiClient ClientFor(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), TestConfiguration.With(("eBirdApi:ApiKey", "test-key")));
}
