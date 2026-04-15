using System.Text.Json;
using System.Text.Json.Serialization;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace eBird.Ingestor.Infrastructure.Services;

/// <summary>
/// HTTP client for the iNaturalist API v1.
/// Mirrors EbirdApiClient in structure and error handling.
/// Rate limit: iNat allows 60 req/min — we throttle to ~54/min via Task.Delay(1100ms).
/// </summary>
public class INatApiClient : IINatApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    // iNat rate-limit semaphore: only 1 request at a time with delay between calls
    private static readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    private const string BaseUrl = "https://api.inaturalist.org/v1";
    private const string UserAgent = "Birdetector/1.0 (birdetector.com)";

    public INatApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<IEnumerable<INatObservationDto>> GetRecentObservationsAsync(
        int inatPlaceId, int daysAgo, int page = 1)
    {
        var d1 = DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-dd");

        var url = $"{BaseUrl}/observations" +
                  $"?iconic_taxa=Aves" +
                  $"&quality_grade=research" +
                  $"&has[]=geo" +
                  $"&place_id={inatPlaceId}" +
                  $"&d1={d1}" +
                  $"&per_page=200" +
                  $"&page={page}" +
                  $"&order_by=created_at" +
                  $"&order=desc";

        var json = await ExecuteWithRateLimitAsync(url);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results))
            return Enumerable.Empty<INatObservationDto>();

        var observations = JsonSerializer.Deserialize<List<INatObservationDto>>(
            results.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Guard: only return actual bird observations
        return (observations ?? new List<INatObservationDto>())
            .Where(o => o.Taxon?.IconicTaxonName == "Aves"
                     && o.Geojson?.Coordinates?.Length >= 2
                     && !string.IsNullOrWhiteSpace(o.ObservedOn));
    }

    public async Task<string?> GetTaxonLocalizedNameAsync(int taxonId, string locale)
    {
        var url = $"{BaseUrl}/taxa/{taxonId}?locale={locale}";

        try
        {
            var json = await ExecuteWithRateLimitAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.GetArrayLength() > 0)
            {
                var taxon = results[0];
                if (taxon.TryGetProperty("preferred_common_name", out var name) &&
                    name.ValueKind == JsonValueKind.String)
                {
                    return name.GetString();
                }
            }
        }
        catch
        {
            // Localized name is optional — don't fail the ingestion
        }

        return null;
    }

    private async Task<string> ExecuteWithRateLimitAsync(string url)
    {
        await _rateLimitSemaphore.WaitAsync();
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

            var policy = GetRetryPolicy();
            var response = await policy.ExecuteAsync(() => client.GetAsync(url));
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            await Task.Delay(1100); // ~54 requests/min — safely under 60/min limit
            _rateLimitSemaphore.Release();
        }
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => (int)msg.StatusCode == 429) // Too Many Requests
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) * 5)); // 5s, 10s, 20s
    }
}
