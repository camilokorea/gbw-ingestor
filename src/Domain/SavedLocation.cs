namespace eBird.Ingestor.Domain;

/// <summary>
/// A coordinate a user has saved. API-owned data, ingestor-owned schema.
/// See issue camilokorea/gbw-api#5.
/// </summary>
public class SavedLocation
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = default!;
    public string Label { get; set; } = default!;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
