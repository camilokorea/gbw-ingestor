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
        _logger.LogInformation("Seeding the ingestion queue...");

        // Clear existing queue
        await _dbContext.IngestionQueue.ExecuteDeleteAsync();

        var allCountryCodes = _configuration.GetSection("CountriesToSeed").Get<List<string>>();
        if (allCountryCodes == null || !allCountryCodes.Any())
        {
            _logger.LogWarning("No country codes found in configuration for seeding.");
            return;
        }

        var queueItems = allCountryCodes.Select(code => new IngestionQueue
        {
            RegionCode = code,
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

        var queueItems = await _dbContext.IngestionQueue
            .Where(i => i.Status == IngestionStatus.Queued || i.Status == IngestionStatus.Failed)
            .ToListAsync();

        if (!queueItems.Any())
            return false;
        else
            return true;
    }
}
