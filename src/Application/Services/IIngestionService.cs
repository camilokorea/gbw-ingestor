namespace eBird.Ingestor.Application.Services;

public interface IIngestionService
{
    /// <summary>Processes the next batch of queued regions; returns the observations saved.</summary>
    Task<int> ProcessQueueAsync();
}