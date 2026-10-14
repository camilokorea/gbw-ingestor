using Microsoft.Extensions.Logging;

namespace Flickr.Synch.Infrastructure.Services;

internal static class FlickrHttp
{
    private const int MaxRetries = 10;

    /// <summary>
    /// Sends GET request with automatic retry on HTTP 429, using Retry-After header or
    /// exponential backoff (2^attempt seconds). Returns null if all retries exhausted.
    /// </summary>
    public static async Task<HttpResponseMessage?> GetWithRetryAsync(
        HttpClient client,
        string url,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
                return response;

            if ((int)response.StatusCode == 429)
            {
                // Respect Retry-After header if present, else exponential backoff
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));

                logger.LogWarning(
                    "HTTP 429 received. Waiting {Seconds}s before retry {Attempt}/{Max}.",
                    retryAfter.TotalSeconds, attempt + 1, MaxRetries);

                await Task.Delay(retryAfter, cancellationToken);
                continue;
            }

            // Non-retryable error. The URL carries the API key, so log only the method.
            logger.LogError("Unrecoverable HTTP {Status} for Flickr method {Method}",
                (int)response.StatusCode, MethodOf(url));
            return null;
        }

        return null;
    }

    private static string MethodOf(string url)
    {
        var marker = url.IndexOf("method=", StringComparison.Ordinal);
        if (marker < 0) return "?";
        var end = url.IndexOf('&', marker);
        return end < 0 ? url[(marker + 7)..] : url[(marker + 7)..end];
    }
}
