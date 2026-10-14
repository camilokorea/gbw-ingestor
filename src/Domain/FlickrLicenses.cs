namespace eBird.Ingestor.Domain;

/// <summary>What a license lets a third party (Birdetector) do with someone else's photo.</summary>
public enum LicenseReuse
{
    /// <summary>Reuse and adapt with credit, commercial or not (CC BY, CC BY-SA) or no restrictions at all.</summary>
    Open,
    /// <summary>Reuse with credit, non-commercial only (any CC NC variant without ND).</summary>
    NonCommercial,
    /// <summary>Reuse only unmodified (any ND variant): cropping or overlaying it on a card is not allowed.</summary>
    NoDerivatives,
    /// <summary>All rights reserved, or a license id Flickr has not documented.</summary>
    Reserved,
}

public sealed record FlickrLicense(int Id, string Name, string Url, LicenseReuse Reuse);

/// <summary>
/// Flickr's license ids, verbatim from flickr.photos.licenses.getInfo (checked 2026-10-14). Group membership
/// (e.g. Avibase's pool) grants rights only to the group's owner, so this per-photo license is what decides
/// whether Birdetector may republish a photo.
/// </summary>
public static class FlickrLicenses
{
    private static readonly Dictionary<int, FlickrLicense> ById = new FlickrLicense[]
    {
        new(0, "All Rights Reserved", "https://www.flickrhelp.com/hc/en-us/articles/10710266545556-Using-Flickr-images-shared-by-other-members", LicenseReuse.Reserved),
        new(1, "CC BY-NC-SA 2.0", "https://creativecommons.org/licenses/by-nc-sa/2.0/", LicenseReuse.NonCommercial),
        new(2, "CC BY-NC 2.0", "https://creativecommons.org/licenses/by-nc/2.0/", LicenseReuse.NonCommercial),
        new(3, "CC BY-NC-ND 2.0", "https://creativecommons.org/licenses/by-nc-nd/2.0/", LicenseReuse.NoDerivatives),
        new(4, "CC BY 2.0", "https://creativecommons.org/licenses/by/2.0/", LicenseReuse.Open),
        new(5, "CC BY-SA 2.0", "https://creativecommons.org/licenses/by-sa/2.0/", LicenseReuse.Open),
        new(6, "CC BY-ND 2.0", "https://creativecommons.org/licenses/by-nd/2.0/", LicenseReuse.NoDerivatives),
        new(7, "No known copyright restrictions", "https://www.flickr.com/commons/usage/", LicenseReuse.Open),
        new(8, "United States Government Work", "https://www.usa.gov/government-copyright", LicenseReuse.Open),
        new(9, "Public Domain Dedication (CC0)", "https://creativecommons.org/publicdomain/zero/1.0/", LicenseReuse.Open),
        new(10, "Public Domain Mark", "https://creativecommons.org/publicdomain/mark/1.0/", LicenseReuse.Open),
        new(11, "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/", LicenseReuse.Open),
        new(12, "CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/", LicenseReuse.Open),
        new(13, "CC BY-ND 4.0", "https://creativecommons.org/licenses/by-nd/4.0/", LicenseReuse.NoDerivatives),
        new(14, "CC BY-NC 4.0", "https://creativecommons.org/licenses/by-nc/4.0/", LicenseReuse.NonCommercial),
        new(15, "CC BY-NC-SA 4.0", "https://creativecommons.org/licenses/by-nc-sa/4.0/", LicenseReuse.NonCommercial),
        new(16, "CC BY-NC-ND 4.0", "https://creativecommons.org/licenses/by-nc-nd/4.0/", LicenseReuse.NoDerivatives),
    }.ToDictionary(l => l.Id);

    public static IReadOnlyCollection<FlickrLicense> All => ById.Values;

    /// <summary>The license for <paramref name="id"/>, or null when it is unknown (not fetched or undocumented).</summary>
    public static FlickrLicense? Find(int? id) => id is { } value && ById.TryGetValue(value, out var license) ? license : null;

    /// <summary>Unknown ids count as reserved: never republish a photo whose terms we cannot name.</summary>
    public static LicenseReuse ReuseOf(int? id) => Find(id)?.Reuse ?? LicenseReuse.Reserved;

    /// <summary>
    /// Parses the "license" field Flickr returns with extras=license (a numeric string such as "4").
    /// Null for anything else, so a malformed value reads as "not fetched" rather than as a wrong license.
    /// </summary>
    public static int? Parse(string? raw) => int.TryParse(raw, out var id) && id >= 0 ? id : null;
}
