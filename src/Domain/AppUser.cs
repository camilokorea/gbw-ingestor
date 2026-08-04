namespace eBird.Ingestor.Domain;

/// <summary>
/// Application user account. Owned semantically by <c>gbw-api</c> (which handles
/// Firebase sign-in and writes the rows), but the schema lives here because the
/// ingestor is the single owner of the <c>eBirdIngestor</c> database migrations.
/// See issue camilokorea/gbw-api#5.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    public string FirebaseUid { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SavedLocation> SavedLocations { get; set; } = new List<SavedLocation>();
    public ICollection<SavedTarget> SavedTargets { get; set; } = new List<SavedTarget>();
}
