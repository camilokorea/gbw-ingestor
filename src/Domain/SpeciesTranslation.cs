namespace eBird.Ingestor.Domain;

/// <summary>
/// Represents a localized common name for a species.
/// </summary>
public class SpeciesTranslation
{
    public int Id { get; set; }

    public int SpeciesId { get; set; }

    /// <summary>
    /// ISO 639-1 language code (e.g., "en", "es", "pt", "fr").
    /// </summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// The localized common name.
    /// </summary>
    public string CommonName { get; set; } = string.Empty;

    public Species Species { get; set; } = null!;
}
