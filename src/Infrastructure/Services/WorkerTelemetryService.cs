using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Infrastructure.Services;

public class WorkerTelemetryService : IWorkerTelemetry
{
    private readonly EbirdIngestorDbContext _db;
    private readonly TelegramAlertService _telegram;
    private readonly ILogger<WorkerTelemetryService> _logger;

    public WorkerTelemetryService(
        EbirdIngestorDbContext db,
        TelegramAlertService telegram,
        ILogger<WorkerTelemetryService> logger)
    {
        _db = db;
        _telegram = telegram;
        _logger = logger;
    }

    public async Task<long> StartAsync(string workerName)
    {
        try
        {
            var run = new WorkerRun
            {
                WorkerName = workerName,
                StartedAt = DateTime.UtcNow,
                Status = "running"
            };
            _db.WorkerRuns.Add(run);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Telemetry: {Worker} started (RunId={RunId})", workerName, run.Id);
            return run.Id;
        }
        catch (Exception ex)
        {
            // Never crash the worker because of telemetry
            _logger.LogWarning(ex, "Telemetry: failed to record start for {Worker}", workerName);
            return -1;
        }
    }

    public async Task CompleteAsync(long runId, int recordsProcessed = 0)
    {
        if (runId < 0) return;
        try
        {
            var run = await _db.WorkerRuns.FindAsync(runId);
            if (run == null) return;
            run.FinishedAt = DateTime.UtcNow;
            run.Status = "success";
            run.RecordsProcessed = recordsProcessed;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Telemetry: RunId={RunId} completed ({Records} records)", runId, recordsProcessed);

            // Purge runs older than 30 days (runs in the background, best-effort)
            _ = PurgeOldRunsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry: failed to record completion for RunId={RunId}", runId);
        }
    }

    public async Task FailAsync(long runId, Exception ex)
    {
        if (runId >= 0)
        {
            try
            {
                var run = await _db.WorkerRuns.FindAsync(runId);
                if (run != null)
                {
                    run.FinishedAt = DateTime.UtcNow;
                    run.Status = "failed";
                    run.ErrorMessage = ex.Message.Length > 500
                        ? ex.Message[..500]
                        : ex.Message;
                    run.ErrorDetail = ex.ToString();
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Telemetry: failed to record failure for RunId={RunId}", runId);
            }
        }

        // Always attempt the Telegram alert even if DB write failed
        await _telegram.SendAlertAsync(runId, ex);
    }

    private async Task PurgeOldRunsAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var old = _db.WorkerRuns.Where(r => r.StartedAt < cutoff).ToList();
            if (old.Count > 0)
            {
                _db.WorkerRuns.RemoveRange(old);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Telemetry: purged {Count} old WorkerRun rows", old.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry: failed to purge old runs");
        }
    }
}
