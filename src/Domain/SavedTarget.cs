namespace eBird.Ingestor.Domain;

/// <summary>
/// A "quest" a user is tracking. API-owned data, ingestor-owned schema.
/// See issue camilokorea/gbw-api#5.
/// </summary>
public class SavedTarget
{
    public int Id { get; set; }

    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;

    // Exactly one of the following will be set to define the target scope
    public int? SpeciesId { get; set; }
    public Species? Species { get; set; }

    public string? FamilySciName { get; set; }

    public string? SearchTerm { get; set; }

    // User's custom label for the search parameter, e.g., "Quest for Eagles"
    public string Label { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
