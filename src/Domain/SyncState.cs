using System.ComponentModel.DataAnnotations;

namespace eBird.Ingestor.Domain;

/// <summary>
/// Persists the last successful synchronization state for various external APIs.
/// </summary>
public class SyncState
{
    [Key]
    public string Key { get; set; } = string.Empty;

    /// <summary>The Unix timestamp (seconds) or date of the last successful sync.</summary>
    public long LastSyncValue { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
