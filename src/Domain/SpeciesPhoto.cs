namespace eBird.Ingestor.Domain;

/// <summary>
/// Join entity linking a Species to a FlickrPhoto.
/// Created by the Flickr.Matcher worker when a photo title
/// contains the species' scientific name.
/// </summary>
public class SpeciesPhoto
{
    /// <summary>FK to Species.</summary>
    public int SpeciesId { get; set; }

    /// <summary>FK to FlickrPhoto.</summary>
    public int FlickrPhotoId { get; set; }

    /// <summary>Navigation property to the Species.</summary>
    public Species Species { get; set; } = null!;

    /// <summary>Navigation property to the FlickrPhoto.</summary>
    public FlickrPhoto FlickrPhoto { get; set; } = null!;
}
