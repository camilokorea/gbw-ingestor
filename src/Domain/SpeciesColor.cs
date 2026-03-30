namespace eBird.Ingestor.Domain;

/// <summary>
/// Stores a dominant color extracted from a species' clipart image via K-means clustering.
/// Each species has up to 10 color records ranked by coverage percentage.
/// </summary>
public class SpeciesColor
{
    public int Id { get; set; }

    /// <summary>Foreign key to Species.</summary>
    public int SpeciesId { get; set; }

    /// <summary>1 = most dominant color, 10 = least dominant.</summary>
    public int Rank { get; set; }

    /// <summary>Hex color string, e.g. "#1A2B3C".</summary>
    public string HexColor { get; set; } = string.Empty;

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    /// <summary>Percentage of non-background pixels this color represents (0.0–100.0).</summary>
    public double CoveragePercent { get; set; }

    public Species Species { get; set; } = null!;
}
