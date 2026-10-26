using System.Text.Json;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using Microsoft.Extensions.Configuration;
using Polly;

namespace eBird.Ingestor.Infrastructure.Services;

/// <summary>
/// eBird API v2. Calls are paced (one at a time, at least <see cref="MinInterval"/> apart, across every client in the
/// process) and a 429 is retried after Retry-After or a growing wait: unpaced bursts of location lookups drew 429s,
/// which failed whole regions on every run until they used up their attempts (June 2026).
/// </summary>
public class EbirdApiClient : IEbirdApiClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    /// <summary>Minimum gap between two eBird calls (eBirdApi:MinIntervalMs, default 1000).</summary>
    public TimeSpan MinInterval { get; set; }
    /// <summary>Wait before the first retry of a 429 without Retry-After; doubles each time (eBirdApi:RateLimitBackoffSeconds, default 15).</summary>
    public TimeSpan RateLimitBackoff { get; set; }
    /// <summary>Retries of a 429 or a transient server error before giving up on the call.</summary>
    public int MaxRetries { get; set; } = 5;

    public EbirdApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        MinInterval = TimeSpan.FromMilliseconds(configuration.GetValue("eBirdApi:MinIntervalMs", 1000));
        RateLimitBackoff = TimeSpan.FromSeconds(configuration.GetValue("eBirdApi:RateLimitBackoffSeconds", 15));
    }

    public async Task<IEnumerable<EbirdObservationDto>> GetRecentObservationsAsync(string regionCode)
    {
        var apiKey = _configuration["eBirdApi:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("La clave de la API de eBird no está configurada en appsettings.json");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

        var url = $"https://api.ebird.org/v2/data/obs/{regionCode}/recent";

        var response = await SendAsync(client, url);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var observations = JsonSerializer.Deserialize<List<EbirdObservationDto>>(json);

        return observations ?? new List<EbirdObservationDto>();
    }

    public async Task<IEnumerable<EbirdObservationDto>> GetHistoricObservationsAsync(string regionCode, DateOnly date)
    {
        var apiKey = _configuration["eBirdApi:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("La clave de la API de eBird no está configurada en appsettings.json");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

        var url = $"https://api.ebird.org/v2/data/obs/{regionCode}/historic/{date.Year}/{date.Month}/{date.Day}?detail=full";
        // A 5xx here is eBird timing out on a big region (always at 60 s), not a blip: retrying only waits longer.
        var response = await SendAsync(client, url, retryServerErrors: false);
        if ((int)response.StatusCode >= 500) throw new RegionTooLargeException(regionCode);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<EbirdObservationDto>>(json) ?? new List<EbirdObservationDto>();
    }

    public async Task<IReadOnlyList<string>> GetSubregionsAsync(string regionCode)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", _configuration["eBirdApi:ApiKey"]);

        var level = regionCode.Contains('-') ? "subnational2" : "subnational1";
        var response = await SendAsync(client, $"https://api.ebird.org/v2/ref/region/list/{level}/{regionCode}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(r => r.GetProperty("code").GetString()!).ToList();
    }

    public async Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId)
    {
        var apiKey = _configuration["eBirdApi:ApiKey"];
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

        var url = $"https://api.ebird.org/v2/ref/location/info/{locId}";
        var response = await SendAsync(client, url);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var details = JsonSerializer.Deserialize<EbirdLocationDetailsDto>(json);

        return details ?? new EbirdLocationDetailsDto();
    }

    public async Task<string?> GetSpeciesLocalizedNameAsync(string speciesCode, string locale = "es")
    {
        var apiKey = _configuration["eBirdApi:ApiKey"];
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

        var url = $"https://api.ebird.org/v2/ref/taxonomy/ebird?species={speciesCode}&locale={locale}&fmt=json";
        var response = await SendAsync(client, url);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        // The API returns an array of taxonomy objects
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            var firstItem = doc.RootElement[0];
            if (firstItem.TryGetProperty("comName", out var comNameElement) && comNameElement.ValueKind == JsonValueKind.String)
            {
                return comNameElement.GetString();
            }
        }
        
        return null;
    }

    /// <summary>One paced GET, retried on 429, and on 5xx and 408 unless told not to (never on 404: a missing
    /// translation stays missing).</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, bool retryServerErrors = true)
    {
        var policy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>(_ => retryServerErrors)
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                             || (retryServerErrors && ((int)msg.StatusCode >= 500 || msg.StatusCode == System.Net.HttpStatusCode.RequestTimeout)))
            .WaitAndRetryAsync(
                MaxRetries,
                (attempt, outcome, _) => outcome.Result?.Headers.RetryAfter?.Delta
                                         ?? (outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                                             ? RateLimitBackoff * Math.Pow(2, attempt - 1)
                                             : TimeSpan.FromSeconds(Math.Pow(2, attempt))),
                (_, _, _, _) => Task.CompletedTask);

        return await policy.ExecuteAsync(async () =>
        {
            await Gate.WaitAsync();
            try
            {
                var wait = _lastCall + MinInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                _lastCall = DateTime.UtcNow;
            }
            finally
            {
                Gate.Release();
            }
            return await client.GetAsync(url);
        });
    }
}
