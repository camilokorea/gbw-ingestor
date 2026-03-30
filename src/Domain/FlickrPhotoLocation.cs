namespace eBird.Ingestor.Domain;

/// <summary>
/// Represents geolocation metadata for a FlickrPhoto,
/// populated by the Flickr.Geo worker via the flickr.photos.geo.getLocation API.
/// One-to-one with FlickrPhoto (owned entity stored in a separate table).
/// </summary>
public class FlickrPhotoLocation
{
    /// <summary>Database surrogate key.</summary>
    public int Id { get; set; }

    /// <summary>FK to FlickrPhoto.</summary>
    public int FlickrPhotoId { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Flickr accuracy level (1=world, 16=street).</summary>
    public int? Accuracy { get; set; }

    /// <summary>Flickr location context (0=not defined, 1=indoors, 2=outdoors).</summary>
    public int? Context { get; set; }

    public string? PlaceId { get; set; }
    public long? WoeId { get; set; }

    // Locality (city/town)
    public string? LocalityName { get; set; }
    public string? LocalityPlaceId { get; set; }
    public long? LocalityWoeId { get; set; }

    // County
    public string? CountyName { get; set; }
    public string? CountyPlaceId { get; set; }
    public long? CountyWoeId { get; set; }

    // Region (state/province)
    public string? RegionName { get; set; }
    public string? RegionPlaceId { get; set; }
    public long? RegionWoeId { get; set; }

    // Country
    public string? CountryName { get; set; }
    public string? CountryPlaceId { get; set; }
    public long? CountryWoeId { get; set; }

    /// <summary>Navigation property back to the photo.</summary>
    public FlickrPhoto Photo { get; set; } = null!;
}
