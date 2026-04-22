namespace eBird.Ingestor.Domain;

/// <summary>
/// Represents a photo retrieved from iNaturalist observations.
/// </summary>
public class INatPhoto
{
    public int Id { get; set; }

    /// <summary>iNaturalist's own photo ID.</summary>
    public long PhotoId { get; set; }

    /// <summary>The attribution string (e.g. "(c) John Doe, CC-BY-NC").</summary>
    public string Attribution { get; set; } = string.Empty;

    /// <summary>License code (e.g. "cc-by-nc").</summary>
    public string? LicenseCode { get; set; }

    /// <summary>Original iNaturalist observation ID this photo came from.</summary>
    public long? INatObservationId { get; set; }

    // URL variants provided by iNat
    public string SquareUrl { get; set; } = string.Empty;
    public string SmallUrl { get; set; } = string.Empty;
    public string MediumUrl { get; set; } = string.Empty;
    public string LargeUrl { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;

    /// <summary>Link to the observation or photo page on iNat.</summary>
    public string WebUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Species-photo associations.</summary>
    public ICollection<SpeciesINatPhoto> SpeciesPhotos { get; set; } = new List<SpeciesINatPhoto>();
}
