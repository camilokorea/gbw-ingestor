namespace eBird.Ingestor.Domain;

/// <summary>
/// iNaturalist's photo license codes (the "license_code" field), classified with the same <see cref="LicenseReuse"/>
/// as Flickr's. A null code means the observer kept all rights.
/// </summary>
public static class INatLicenses
{
    public static LicenseReuse ReuseOf(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "cc0" or "cc-by" or "cc-by-sa" => LicenseReuse.Open,
        "cc-by-nc" or "cc-by-nc-sa" => LicenseReuse.NonCommercial,
        "cc-by-nd" or "cc-by-nc-nd" => LicenseReuse.NoDerivatives,
        _ => LicenseReuse.Reserved,
    };
}
