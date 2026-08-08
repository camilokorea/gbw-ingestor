using System.Net;

namespace eBird.Ingestor.Tests.TestSupport;

/// <summary>
/// Serves a scripted sequence of responses and records every request that was made, so tests can
/// assert on retry counts, headers and the exact URL a client built.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();

    /// <summary>Response used once the scripted sequence runs out. Defaults to 200 with "[]".</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Fallback { get; set; } =
        _ => Json(HttpStatusCode.OK, "[]");

    /// <summary>Every request seen, in order. Includes retries.</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    public StubHttpMessageHandler RespondWith(HttpStatusCode status, string body)
    {
        _script.Enqueue(_ => Json(status, body));
        return this;
    }

    public StubHttpMessageHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _script.Enqueue(responder);
        return this;
    }

    /// <summary>Simulates a connection-level failure (DNS, reset, TLS).</summary>
    public StubHttpMessageHandler RespondWithNetworkFailure()
    {
        _script.Enqueue(_ => throw new HttpRequestException("Simulated network failure."));
        return this;
    }

    /// <summary>Makes every response from now on use <paramref name="status"/> and <paramref name="body"/>.</summary>
    public StubHttpMessageHandler AlwaysRespondWith(HttpStatusCode status, string body)
    {
        Fallback = _ => Json(status, body);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var responder = _script.Count > 0 ? _script.Dequeue() : Fallback;
        return Task.FromResult(responder(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>
/// Hands out a new <see cref="HttpClient"/> per call over a shared stub handler.
///
/// A new client each time matters: the API clients call <c>DefaultRequestHeaders.Add</c> on every
/// request, which throws on a reused client, and Xeno-Canto assigns <c>Timeout</c> after the
/// client has already been used. Production's <c>IHttpClientFactory</c> behaves the same way.
/// </summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly StubHttpMessageHandler _handler;

    public StubHttpClientFactory(StubHttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
