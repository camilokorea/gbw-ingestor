namespace eBird.Ingestor.Domain;

public class IngestionQueue
{
    public int Id { get; set; }
    public string RegionCode { get; set; }
    public IngestionStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
}

public enum IngestionStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}
