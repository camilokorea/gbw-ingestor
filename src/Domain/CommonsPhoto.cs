namespace eBird.Ingestor.Domain;

/// <summary>
/// A Wikimedia Commons image of a species, found through its Wikidata item (taxon name P225 → image P18).
/// Only images under a license Birdetector may republish are stored, with everything a credit line needs.
/// </summary>
public class CommonsPhoto
{
    public const int MaxArtistLength = 300;

    public int Id { get; set; }

    public int SpeciesId { get; set; }
    public Species Species { get; set; } = null!;

    /// <summary>Wikidata item of the taxon, e.g. "Q47501".</summary>
    public string WikidataId { get; set; } = string.Empty;

    /// <summary>Commons file title with namespace, e.g. "File:Keel billed toucan costa rica (cropped).jpg".</summary>
    public string FileTitle { get; set; } = string.Empty;

    /// <summary>Order of the image on the Wikidata item (0 = first P18 value).</summary>
    public int Rank { get; set; }

    /// <summary>Thumbnail about 1080 px wide, served by upload.wikimedia.org.</summary>
    public string ImageUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The file's description page on Commons (where the credit links to).</summary>
    public string DescriptionUrl { get; set; } = string.Empty;

    /// <summary>Author as plain text (Commons sends HTML).</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>Commons license code, e.g. "cc-by-sa-4.0", "cc0", "pd".</summary>
    public string LicenseCode { get; set; } = string.Empty;
    /// <summary>Human name for credit lines, e.g. "CC BY-SA 4.0".</summary>
    public string LicenseShortName { get; set; } = string.Empty;
    public string? LicenseUrl { get; set; }

    public DateTime FetchedAt { get; set; }
}

/// <summary>Commons license codes (extmetadata "License"), classified like Flickr's and iNaturalist's.</summary>
public static class CommonsLicenses
{
    /// <summary>
    /// Commons only hosts free licenses, but some (GFDL, which requires shipping the license text, or odd custom
    /// templates) are impractical for a social post, so only CC BY, CC BY-SA, CC0 and public domain count as open.
    /// </summary>
    public static LicenseReuse ReuseOf(string? code)
    {
        var c = code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (c is "cc0" or "pd" || c.StartsWith("cc0-") || c.StartsWith("pd-")) return LicenseReuse.Open;
        if (!c.StartsWith("cc-by")) return LicenseReuse.Reserved;
        if (c.Contains("-nd")) return LicenseReuse.NoDerivatives;
        if (c.Contains("-nc")) return LicenseReuse.NonCommercial;
        return LicenseReuse.Open; // cc-by-*, cc-by-sa-*
    }
}
