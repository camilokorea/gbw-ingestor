namespace eBird.Ingestor.Domain;

public class IngestionQueue
{
    /// <summary>
    /// Attempts a region gets per queue cycle. After that it is skipped until the queue is seeded again, and it must
    /// never keep the cycle from ending: that is what froze eBird ingestion from June to October 2026.
    /// </summary>
    public const int MaxAttempts = 10;

    public int Id { get; set; }
    public string RegionCode { get; set; }
    public IngestionStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }

    /// <summary>Which ingestor owns this item: "ebird" or "inat".</summary>
    public string Source { get; set; } = "ebird";
}

public enum IngestionStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}
