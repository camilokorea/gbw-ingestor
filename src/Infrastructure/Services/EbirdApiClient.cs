using System.Text.Json;
using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using Microsoft.Extensions.Configuration;

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

        var response = await client.GetAsync(url);

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
        var response = await client.GetAsync(url);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var details = JsonSerializer.Deserialize<EbirdLocationDetailsDto>(json);

        return details ?? new EbirdLocationDetailsDto();
    }
}