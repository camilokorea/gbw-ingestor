namespace eBird.Ingestor.Application.Services;

public interface IIngestionService
{
    /// <summary>Processes the next batch of queued regions; returns the observations saved.</summary>
    Task<int> ProcessQueueAsync();

    /// <summary>Fills a gap day by day from eBird's historic endpoint; resumable. Returns the observations saved.</summary>
    Task<int> BackfillHistoricAsync(IReadOnlyList<string> regions, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Retries the regions a backfill left out; returns the observations saved.</summary>
    Task<int> BackfillMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether a historic backfill is running (its heartbeat is fresher than <paramref name="freshFor"/>).</summary>
    Task<bool> IsHistoricBackfillActiveAsync(TimeSpan freshFor);
}