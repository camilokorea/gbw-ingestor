using System.Text.Json;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace eBird.Ingestor.Infrastructure.Services;

public class EbirdApiClient : IEbirdApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public EbirdApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
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

        var policy = GetRetryPolicy();
        var response = await policy.ExecuteAsync(() => client.GetAsync(url));

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var observations = JsonSerializer.Deserialize<List<EbirdObservationDto>>(json);

        return observations ?? new List<EbirdObservationDto>();
    }

    public async Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId)
    {
        var apiKey = _configuration["eBirdApi:ApiKey"];
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

        var url = $"https://api.ebird.org/v2/ref/location/info/{locId}";
        var policy = GetRetryPolicy();
        var response = await policy.ExecuteAsync(() => client.GetAsync(url));

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
        var policy = GetRetryPolicy();
        var response = await policy.ExecuteAsync(() => client.GetAsync(url));

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

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}