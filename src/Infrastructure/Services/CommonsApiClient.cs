using System.Net;
using System.Text;
using System.Text.Json;
using eBird.Ingestor.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Infrastructure.Services;

/// <summary>
/// Wikidata Query Service (taxon name → item → image) and the Commons API (image metadata). Both ask bots for a
/// descriptive User-Agent, set on the "Commons" HttpClient, and to back off on 429/503.
/// </summary>
public class CommonsApiClient : ICommonsApiClient
{
    public const string HttpClientName = "Commons";
    private const string SparqlEndpoint = "https://query.wikidata.org/sparql";
    private const string CommonsApi = "https://commons.wikimedia.org/w/api.php";
    private const int MaxRetries = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CommonsApiClient> _logger;

    /// <summary>Base wait before retrying a throttled request (doubles each attempt) when there is no Retry-After.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public CommonsApiClient(IHttpClientFactory httpClientFactory, ILogger<CommonsApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WikidataTaxonImages>> FindTaxonImagesAsync(
        IReadOnlyCollection<string> scientificNames, CancellationToken cancellationToken = default)
    {
        if (scientificNames.Count == 0) return [];

        var values = string.Join(" ", scientificNames.Select(n => $"\"{EscapeSparql(n)}\""));
        // Species-rank items (P105 = Q7432) whose taxon name (P225) matches exactly; images (P18) are optional so a
        // species found without one is still reported as found.
        var query = $$"""
            SELECT ?name ?item ?image WHERE {
              VALUES ?name { {{values}} }
              ?item wdt:P225 ?name ; wdt:P105 wd:Q7432 .
              OPTIONAL { ?item wdt:P18 ?image }
            }
            """;

        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, SparqlEndpoint)
            {
                Content = new FormUrlEncodedContent([new("query", query)]),
            };
            request.Headers.Accept.ParseAdd("application/sparql-results+json");
            return request;
        }, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseSparql(doc.RootElement);
    }

    public async Task<IReadOnlyList<CommonsImageInfo>> GetImageInfoAsync(
        IReadOnlyCollection<string> fileTitles, CancellationToken cancellationToken = default)
    {
        if (fileTitles.Count == 0) return [];
        if (fileTitles.Count > 50) throw new ArgumentException("Commons accepts at most 50 titles per query", nameof(fileTitles));

        var url = CommonsApi +
                  "?action=query&format=json&formatversion=2&prop=imageinfo" +
                  "&iiprop=" + Uri.EscapeDataString("url|size|mime|extmetadata") +
                  "&iiurlwidth=1080" +
                  "&iiextmetadatafilter=" + Uri.EscapeDataString("Artist|LicenseShortName|LicenseUrl|License|Restrictions") +
                  "&titles=" + Uri.EscapeDataString(string.Join("|", fileTitles));

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseImageInfo(doc.RootElement);
    }

    public static IReadOnlyList<WikidataTaxonImages> ParseSparql(JsonElement root)
    {
        var rows = root.GetProperty("results").GetProperty("bindings").EnumerateArray()
            .Select(b => (
                Name: Value(b, "name"),
                Item: Value(b, "item"),
                Image: Value(b, "image")))
            .Where(r => r.Name.Length > 0 && r.Item.Length > 0)
            .ToList();

        // A name can match more than one item (rare duplicates); prefer one with images, then the oldest Q-id.
        return rows
            .GroupBy(r => r.Name)
            .Select(byName =>
            {
                var item = byName
                    .GroupBy(r => r.Item)
                    .OrderByDescending(g => g.Any(r => r.Image.Length > 0))
                    .ThenBy(g => QNumber(g.Key))
                    .First();
                var titles = item
                    .Where(r => r.Image.Length > 0)
                    .Select(r => FileTitleFromFilePath(r.Image))
                    .Distinct()
                    .ToList();
                return new WikidataTaxonImages(byName.Key, item.Key[(item.Key.LastIndexOf('/') + 1)..], titles);
            })
            .ToList();
    }

    public static IReadOnlyList<CommonsImageInfo> ParseImageInfo(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];

        var result = new List<CommonsImageInfo>();
        foreach (var page in pages.EnumerateArray())
        {
            if (page.TryGetProperty("missing", out _) || page.TryGetProperty("invalid", out _)) continue;
            if (!page.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0) continue;

            var info = infos[0];
            var meta = info.TryGetProperty("extmetadata", out var m) ? m : default;
            result.Add(new CommonsImageInfo(
                FileTitle: page.GetProperty("title").GetString() ?? string.Empty,
                ThumbnailUrl: StripQuery(String(info, "thumburl") is { Length: > 0 } thumb ? thumb : String(info, "url")),
                ThumbnailWidth: Int(info, "thumbwidth") ?? Int(info, "width") ?? 0,
                ThumbnailHeight: Int(info, "thumbheight") ?? Int(info, "height") ?? 0,
                DescriptionUrl: String(info, "descriptionurl"),
                Mime: String(info, "mime"),
                ArtistHtml: Meta(meta, "Artist"),
                LicenseCode: Meta(meta, "License"),
                LicenseShortName: Meta(meta, "LicenseShortName"),
                LicenseUrl: Meta(meta, "LicenseUrl") is { Length: > 0 } licenseUrl ? licenseUrl : null,
                Restrictions: Meta(meta, "Restrictions")));
        }
        return result;
    }

    /// <summary>
    /// Wikidata gives images as "http://commons.wikimedia.org/wiki/Special:FilePath/House%20sparrow.jpg";
    /// the Commons API wants "File:House sparrow.jpg".
    /// </summary>
    public static string FileTitleFromFilePath(string url) =>
        "File:" + Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..]).Replace('_', ' ');

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 0; ; attempt++)
        {
            using var request = build();
            var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            var throttled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (!throttled || attempt + 1 >= MaxRetries)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"{request.RequestUri?.Host} answered HTTP {status}", null, response.StatusCode);
            }

            var wait = response.Headers.RetryAfter?.Delta ?? RetryBaseDelay * Math.Pow(2, attempt);
            _logger.LogWarning("{Host} throttled us (HTTP {Status}); retrying in {Seconds}s.",
                request.RequestUri?.Host, (int)response.StatusCode, wait.TotalSeconds);
            response.Dispose();
            await Task.Delay(wait, cancellationToken);
        }
    }

    private static string EscapeSparql(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => @"\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    private static long QNumber(string itemUri) =>
        long.TryParse(itemUri[(itemUri.LastIndexOf('/') + 2)..], out var n) ? n : long.MaxValue;

    private static string StripQuery(string url) => url.Split('?')[0];

    private static string Value(JsonElement binding, string name) =>
        binding.TryGetProperty(name, out var v) && v.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "";

    private static string String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string Meta(JsonElement meta, string name) =>
        meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(name, out var field) &&
        field.TryGetProperty("value", out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
            : "";
}
