namespace eBird.Ingestor.Domain;

public class IngestionQueue
{
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
