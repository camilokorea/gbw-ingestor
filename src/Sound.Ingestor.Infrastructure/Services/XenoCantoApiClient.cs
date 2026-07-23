using Microsoft.Extensions.Configuration;
using Sound.Ingestor.Application.Contracts.Api;
using Sound.Ingestor.Application.Contracts.Dtos;
using System.Net.Http.Json;

namespace Sound.Ingestor.Infrastructure.Services;

public class XenoCantoApiClient : IXenoCantoApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    // Xeno-Canto retired API v2 (returns 404). v3 lives on the apex domain and requires an API key.
    private const string BaseUrl = "https://xeno-canto.org/api/3/recordings";

    public XenoCantoApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<XenoCantoResponseDto> GetRecordingsAsync(string scientificName)
    {
        var apiKey = _configuration["XenoCantoApi:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("La clave de la API de Xeno-Canto no está configurada en appsettings.json (XenoCantoApi:ApiKey).");
        }

        var client = _httpClientFactory.CreateClient();
        var query = BuildQuery(scientificName);
        var url = $"{BaseUrl}?query={Uri.EscapeDataString(query)}&key={Uri.EscapeDataString(apiKey)}";

        try
        {
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<XenoCantoResponseDto>();
            return result ?? new XenoCantoResponseDto();
        }
        catch (HttpRequestException ex)
        {
            // Log the exception (optional, requires ILogger)
            Console.WriteLine($"Error fetching data from Xeno-Canto API: {ex.Message}");
            return new XenoCantoResponseDto(); // Return empty response on failure
        }
    }

    /// <summary>
    /// Builds a v3 structured query from a scientific name. v3 replaced free-text search with
    /// tag grammar, so a binomial like "Molothrus aeneus" becomes <c>gen:Molothrus sp:aeneus</c>.
    /// Taxonomy tags must appear first in the query.
    /// </summary>
    private static string BuildQuery(string scientificName)
    {
        var parts = (scientificName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var tags = new List<string> { $"gen:{parts[0]}" };
        if (parts.Length >= 2)
        {
            tags.Add($"sp:{parts[1]}");
        }
        if (parts.Length >= 3)
        {
            tags.Add($"ssp:{parts[2]}");
        }

        return string.Join(' ', tags);
    }
}
