using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Domain;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace eBird.Ingestor.Infrastructure.Services;

public class INatPhotoService : IINatPhotoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<INatPhotoService> _logger;

    public INatPhotoService(IHttpClientFactory httpClientFactory, ILogger<INatPhotoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<INatPhoto>> GetPhotosForTaxonAsync(long taxonId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("INat");
        // We look for Research Grade observations for this taxon, sorted by most faved/quality.
        var url = $"https://api.inaturalist.org/v1/observations?taxon_id={taxonId}&quality_grade=research&per_page=5&order_by=votes&photos=true";

        try
        {
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return new List<INatPhoto>();
            }

            var photos = new List<INatPhoto>();

            foreach (var observation in results.EnumerateArray())
            {
                if (!observation.TryGetProperty("photos", out var obsPhotos) || obsPhotos.GetArrayLength() == 0)
                    continue;

                var obsId = observation.GetProperty("id").GetInt64();
                
                foreach (var photo in obsPhotos.EnumerateArray())
                {
                    var id = photo.GetProperty("id").GetInt64();
                    var attribution = GetString(photo, "attribution");
                    var license = GetString(photo, "license_code");
                    var baseUrl = GetString(photo, "url");

                    if (string.IsNullOrEmpty(baseUrl)) continue;

                    // iNat URLs usually look like .../square.jpg. We can swap "square" for "medium", "large", etc.
                    photos.Add(new INatPhoto
                    {
                        PhotoId = id,
                        INatObservationId = obsId,
                        Attribution = attribution,
                        LicenseCode = license,
                        SquareUrl = baseUrl,
                        SmallUrl = baseUrl.Replace("square", "small"),
                        MediumUrl = baseUrl.Replace("square", "medium"),
                        LargeUrl = baseUrl.Replace("square", "large"),
                        OriginalUrl = baseUrl.Replace("square", "original"),
                        WebUrl = $"https://www.inaturalist.org/observations/{obsId}",
                        CreatedAt = DateTime.UtcNow
                    });

                    // For now, we just take the first photo per observation to avoid duplicates
                    break; 
                }
                
                // We only need a few top photos
                if (photos.Count >= 3) break;
            }

            return photos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching iNat photos for taxon {TaxonId}", taxonId);
            return new List<INatPhoto>();
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? string.Empty;
        return string.Empty;
    }
}
