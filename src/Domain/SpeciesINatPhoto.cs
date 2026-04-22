namespace eBird.Ingestor.Domain;

/// <summary>
/// Join table linking a Species to an iNaturalist photo.
/// </summary>
public class SpeciesINatPhoto
{
    public int SpeciesId { get; set; }
    public Species Species { get; set; } = null!;

    public int INatPhotoId { get; set; }
    public INatPhoto INatPhoto { get; set; } = null!;

    /// <summary>Allows prioritizing certain photos for display.</summary>
    public int Rank { get; set; } = 0;
}
