using Sound.Ingestor.Application.Contracts.Api;
using Sound.Ingestor.Application.Contracts.Dtos;
using System.Net.Http.Json;

namespace Sound.Ingestor.Infrastructure.Services;

public class XenoCantoApiClient : IXenoCantoApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string BaseUrl = "https://www.xeno-canto.org/api/2/recordings";

    public XenoCantoApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<XenoCantoResponseDto> GetRecordingsAsync(string scientificName)
    {
        var client = _httpClientFactory.CreateClient();
        var url = $"{BaseUrl}?query={scientificName}";

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
}
