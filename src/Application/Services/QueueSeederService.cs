using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

public class QueueSeederService : IQueueSeederService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<QueueSeederService> _logger;
    private readonly IConfiguration _configuration;

    public QueueSeederService(IApplicationDbContext dbContext, ILogger<QueueSeederService> logger, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SeedQueueAsync()
    {
        _logger.LogInformation("Seeding the eBird ingestion queue...");

        // Only clear eBird items — iNat items are managed by INatQueueSeederService
        var exhausted = await _dbContext.IngestionQueue
            .Where(i => i.Source == "ebird" && i.Status == IngestionStatus.Failed)
            .Select(i => i.RegionCode)
            .ToListAsync();
        if (exhausted.Count > 0)
            _logger.LogWarning("{Count} regions failed every attempt last cycle: {Regions}", exhausted.Count, string.Join(" ", exhausted));

        await _dbContext.IngestionQueue
            .Where(i => i.Source == "ebird")
            .ExecuteDeleteAsync();

        var allCountryCodes = _configuration.GetSection("CountriesToSeed").Get<List<string>>();
        if (allCountryCodes == null || !allCountryCodes.Any())
        {
            _logger.LogWarning("No country codes found in configuration for seeding.");
            return;
        }

        var queueItems = allCountryCodes.Select(code => new IngestionQueue
        {
            RegionCode = code,
            Source = "ebird",
            Status = IngestionStatus.Queued,
            AttemptCount = 0,
            LastAttemptUtc = null
        });

        await _dbContext.IngestionQueue.AddRangeAsync(queueItems);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Queue seeded successfully with {Count} regions.", allCountryCodes.Count);
    }

    public async Task<bool> CheckQueueCompletedAsync()
    {
        _logger.LogInformation("Checking Queue is already processed...");

        // Work left = regions still queued, or failed with attempts to spare. Regions that used up their attempts are
        // done for this cycle; counting them as pending kept the queue from ever being seeded again.
        var pending = await _dbContext.IngestionQueue
            .AnyAsync(i => i.Source == "ebird" &&
                           (i.Status == IngestionStatus.Queued ||
                            (i.Status == IngestionStatus.Failed && i.AttemptCount < IngestionQueue.MaxAttempts)));

        return pending;
    }
}
