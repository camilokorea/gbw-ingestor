using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eBird.Ingestor.Application.Services;

/// <summary>
/// Seeds the shared IngestionQueue with iNat-source items.
/// Mirrors QueueSeederService exactly; only difference is Source = "inat".
/// </summary>
public class INatQueueSeederService : IINatQueueSeederService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<INatQueueSeederService> _logger;
    private readonly IConfiguration _configuration;

    public INatQueueSeederService(
        IApplicationDbContext dbContext,
        ILogger<INatQueueSeederService> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SeedQueueAsync()
    {
        _logger.LogInformation("Seeding the iNaturalist ingestion queue...");

        // Only clear iNat items — eBird items are managed by QueueSeederService
        await _dbContext.IngestionQueue
            .Where(i => i.Source == "inat")
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
            Source = "inat",
            Status = IngestionStatus.Queued,
            AttemptCount = 0,
            LastAttemptUtc = null
        });

        await _dbContext.IngestionQueue.AddRangeAsync(queueItems);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("iNat queue seeded with {Count} regions.", allCountryCodes.Count);
    }

    public async Task<bool> CheckQueueCompletedAsync()
    {
        _logger.LogInformation("Checking iNat queue is already processed...");

        var queueItems = await _dbContext.IngestionQueue
            .Where(i => i.Source == "inat" &&
                       (i.Status == IngestionStatus.Queued || i.Status == IngestionStatus.Failed))
            .ToListAsync();

        return queueItems.Any();
    }
}
