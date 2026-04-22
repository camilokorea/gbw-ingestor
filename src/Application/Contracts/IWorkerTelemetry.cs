namespace eBird.Ingestor.Application.Contracts;

/// <summary>
/// Records worker execution telemetry and sends failure alerts.
/// Implementations are deliberately fire-and-forget — a telemetry failure
/// must never crash the worker.
/// </summary>
public interface IWorkerTelemetry
{
    /// <summary>
    /// Call at the very start of a worker run.
    /// Returns a runId to pass to CompleteAsync / FailAsync.
    /// </summary>
    Task<long> StartAsync(string workerName);

    /// <summary>
    /// Call when the worker finishes successfully.
    /// </summary>
    Task CompleteAsync(long runId, int recordsProcessed = 0);

    /// <summary>
    /// Call inside the catch block. Persists the error and sends a Telegram alert.
    /// </summary>
    Task FailAsync(long runId, Exception ex);
}
