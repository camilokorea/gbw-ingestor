namespace eBird.Ingestor.Application.Contracts;

/// <summary>A taxon's Wikidata item and its images (P18 values, in order).</summary>
public sealed record WikidataTaxonImages(string ScientificName, string WikidataId, IReadOnlyList<string> FileTitles);

/// <summary>What Commons says about one file: URLs, size and license metadata (Artist is still HTML).</summary>
public sealed record CommonsImageInfo(
    string FileTitle,
    string ThumbnailUrl,
    int ThumbnailWidth,
    int ThumbnailHeight,
    string DescriptionUrl,
    string Mime,
    string ArtistHtml,
    string LicenseCode,
    string LicenseShortName,
    string? LicenseUrl,
    string Restrictions);

public interface ICommonsApiClient
{
    /// <summary>
    /// Looks scientific names up on Wikidata (species-rank items whose taxon name matches exactly). Names with no
    /// item are absent from the result. One SPARQL query per call, so callers batch (a few hundred names).
    /// </summary>
    Task<IReadOnlyList<WikidataTaxonImages>> FindTaxonImagesAsync(
        IReadOnlyCollection<string> scientificNames, CancellationToken cancellationToken = default);

    /// <summary>Commons metadata for up to 50 file titles; missing files are absent from the result.</summary>
    Task<IReadOnlyList<CommonsImageInfo>> GetImageInfoAsync(
        IReadOnlyCollection<string> fileTitles, CancellationToken cancellationToken = default);
}
