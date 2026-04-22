namespace eBird.Ingestor.Domain;

/// <summary>
/// Records a single execution of any ingestor worker for dashboard monitoring.
/// Rows older than 30 days are purged automatically by the ebird-ingestor worker.
/// </summary>
public class WorkerRun
{
    public long Id { get; set; }

    /// <summary>
    /// Identifies which worker ran. E.g. "inat-photo", "flickr-synch", "ebird-ingestor".
    /// </summary>
    public string WorkerName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// "running" | "success" | "failed"
    /// </summary>
    public string Status { get; set; } = "running";

    public int? RecordsProcessed { get; set; }

    /// <summary>
    /// Short error message (ex.Message).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full stack trace for debugging.
    /// </summary>
    public string? ErrorDetail { get; set; }
}
