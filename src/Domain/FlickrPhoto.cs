namespace eBird.Ingestor.Domain;

/// <summary>
/// Represents a photo retrieved from a Flickr group pool.
/// </summary>
public class FlickrPhoto
{
    /// <summary>Database surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>Flickr's own numeric photo ID. Unique index.</summary>
    public long PhotoId { get; set; }

    /// <summary>Photo title as stored in Flickr.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Display name of the photo owner.</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Flickr user ID of the owner (e.g. "12345678@N00").</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>URL to the photo's page on Flickr.</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>Square thumbnail (75×75).</summary>
    public string SquareThumbnailUrl { get; set; } = string.Empty;

    /// <summary>Large square thumbnail (150×150).</summary>
    public string LargeSquareThumbnailUrl { get; set; } = string.Empty;

    /// <summary>Thumbnail (100px on longest side).</summary>
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>Small image (240px on longest side).</summary>
    public string SmallUrl { get; set; } = string.Empty;

    /// <summary>Small image (320px on longest side). Used for health-check in Eraser.</summary>
    public string Small320Url { get; set; } = string.Empty;

    /// <summary>Medium image (500px on longest side).</summary>
    public string MediumUrl { get; set; } = string.Empty;

    /// <summary>Medium image (640px on longest side).</summary>
    public string Medium640Url { get; set; } = string.Empty;

    /// <summary>Medium image (800px on longest side).</summary>
    public string Medium800Url { get; set; } = string.Empty;

    /// <summary>Large image (1024px on longest side).</summary>
    public string LargeUrl { get; set; } = string.Empty;

    /// <summary>Space-delimited tags from Flickr.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Flickr license id (see <see cref="FlickrLicenses"/>). Null = not fetched yet: photos synced before
    /// licenses were recorded, until the FlickrLicenseBackfill tool fills them in.
    /// </summary>
    public int? LicenseId { get; set; }

    /// <summary>Optional geolocation data fetched by the Geo worker.</summary>
    public FlickrPhotoLocation? Location { get; set; }

    /// <summary>Species-photo associations created by the Matcher worker.</summary>
    public ICollection<SpeciesPhoto> SpeciesPhotos { get; set; } = new List<SpeciesPhoto>();
}
