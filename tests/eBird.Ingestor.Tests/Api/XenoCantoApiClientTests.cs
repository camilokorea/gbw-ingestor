using System.Net;
using eBird.Ingestor.Tests.TestSupport;
using Sound.Ingestor.Infrastructure.Services;
using Xunit;

namespace eBird.Ingestor.Tests.Api;

/// <summary>
/// Xeno-Canto v3 dropped free-text search for a tag grammar, so a binomial has to be taken apart
/// into <c>gen:</c>/<c>sp:</c>/<c>ssp:</c> tags. Get that wrong and the API answers 200 with an
/// empty list — no species gets a recording and nothing anywhere reports a problem.
/// </summary>
public class XenoCantoApiClientTests
{
    [Fact]
    public async Task RefusesToCallXenoCantoWithoutAnApiKey()
    {
        var handler = new StubHttpMessageHandler();
        var client = new XenoCantoApiClient(new StubHttpClientFactory(handler), TestConfiguration.Empty());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetRecordingsAsync("Ramphastos sulfuratus"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("Ramphastos sulfuratus", "gen:Ramphastos sp:sulfuratus")]
    [InlineData("Ramphastos sulfuratus brevicarinatus", "gen:Ramphastos sp:sulfuratus ssp:brevicarinatus")]
    [InlineData("Ramphastos", "gen:Ramphastos")]
    [InlineData("  Ramphastos   sulfuratus  ", "gen:Ramphastos sp:sulfuratus")]
    public async Task TranslatesAScientificNameIntoTheV3TagGrammar(string scientificName, string expectedQuery)
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("xeno-canto-recordings.json"));

        await ClientFor(handler).GetRecordingsAsync(scientificName);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains($"query={expectedQuery}", query);
        Assert.Contains("key=test-key", query);
    }

    [Fact]
    public async Task CallsTheV3EndpointOnTheApexDomain()
    {
        // v2 was retired and answers 404 on the api. subdomain; the retry policy would hide that
        // as three more 404s and an empty result.
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("xeno-canto-recordings.json"));

        await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus");

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("https://xeno-canto.org/api/3/recordings", uri.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task ParsesARecordedRecordingsResponse()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, Fixture.Load("xeno-canto-recordings.json"));

        var response = await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus");

        Assert.Equal(2, response.Recordings.Count);

        var song = response.Recordings[0];
        Assert.Equal("1001", song.Id);
        Assert.Equal("song", song.Type);
        Assert.Equal("A", song.Quality);
        Assert.Equal("//xeno-canto.org/1001/download", song.AudioUrl);
        Assert.Equal("//creativecommons.org/licenses/by-nc-sa/4.0/", song.License);
    }

    [Fact]
    public async Task ReturnsAnEmptyResponseWhenTheBodyIsNull()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, "null");

        Assert.Empty((await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus")).Recordings);
    }

    [Fact]
    public async Task ReturnsAnEmptyResponseWhenTheApiKeyIsRejected()
    {
        // Partial failure by design: one species without recordings must not abort a run that
        // walks the entire species table.
        var handler = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.Unauthorized, "");

        Assert.Empty((await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus")).Recordings);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Documents_ARateLimitedResponseYieldsNothingAndIsNotRetried()
    {
        // 429 is outside Polly's transient set here, and the catch-all below turns the resulting
        // exception into an empty response. A throttled run therefore looks exactly like a run
        // where Xeno-Canto genuinely had no recordings for any species.
        var handler = new StubHttpMessageHandler().AlwaysRespondWith(HttpStatusCode.TooManyRequests, "");

        Assert.Empty((await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus")).Recordings);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RecoversFromATransientServerErrorByRetrying()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.BadGateway, "")
            .RespondWith(HttpStatusCode.OK, Fixture.Load("xeno-canto-recordings.json"));

        var response = await ClientFor(handler).GetRecordingsAsync("Ramphastos sulfuratus");

        Assert.Equal(2, response.Recordings.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    private static XenoCantoApiClient ClientFor(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), TestConfiguration.With(("XenoCantoApi:ApiKey", "test-key")));
}
