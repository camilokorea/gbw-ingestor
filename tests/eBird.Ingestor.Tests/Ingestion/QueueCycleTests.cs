using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Ingestion;

/// <summary>
/// The queue cycle: when nothing processable is left, the worker seeds the queue again. On 2026-06-15, 13 eBird regions
/// used up their 10 attempts; the "is there work left" check still counted them, so the queue was never seeded again,
/// while the processor skipped them, and eBird ingestion stopped for four months while every run reported success.
/// </summary>
public class QueueCycleTests
{
    [Theory]
    [InlineData("ebird")]
    [InlineData("inat")]
    public async Task Regions_that_used_up_their_attempts_do_not_count_as_work_left(string source)
    {
        using var db = TestDatabase.Create();
        await AddAsync(db, source, ("US", IngestionStatus.Failed, IngestionQueue.MaxAttempts), ("CR", IngestionStatus.Completed, 1));

        Assert.False(await HasWorkLeftAsync(db, source));
    }

    [Theory]
    [InlineData("ebird", IngestionStatus.Queued, 0)]
    [InlineData("ebird", IngestionStatus.Failed, 9)]
    [InlineData("inat", IngestionStatus.Queued, 0)]
    [InlineData("inat", IngestionStatus.Failed, 9)]
    public async Task Queued_regions_and_failed_ones_with_attempts_left_are_work_left(string source, IngestionStatus status, int attempts)
    {
        using var db = TestDatabase.Create();
        await AddAsync(db, source, ("US", status, attempts));

        Assert.True(await HasWorkLeftAsync(db, source));
    }

    [Fact]
    public async Task The_other_sources_items_are_ignored()
    {
        using var db = TestDatabase.Create();
        await AddAsync(db, "inat", ("US", IngestionStatus.Queued, 0));

        Assert.False(await HasWorkLeftAsync(db, "ebird"));
    }

    [Fact]
    public async Task After_exhausted_failures_the_next_run_seeds_again_and_ingests()
    {
        using var db = TestDatabase.Create();
        await AddAsync(db, "ebird", ("US", IngestionStatus.Failed, IngestionQueue.MaxAttempts));
        var api = new FakeEbirdApiClient();
        api.ObservationsByRegion["US"] = [new EbirdObservationDto
        {
            SpeciesCode = "amerob", ComName = "American Robin", SciName = "Turdus migratorius",
            LocId = "L1", LocName = "Central Park", ObsDt = "2026-10-24 08:00", HowMany = 2, Lat = 40.78, Lng = -73.96,
        }];

        // What the worker's Program does on each run.
        using (var context = db.NewContext())
        {
            var seeder = new QueueSeederService(context, NullLogger<QueueSeederService>.Instance,
                TestConfiguration.With(("CountriesToSeed:0", "US")));
            if (!await seeder.CheckQueueCompletedAsync()) await seeder.SeedQueueAsync();
        }
        int saved;
        using (var context = db.NewContext())
        {
            saved = await new IngestionService(api, context, NullLogger<IngestionService>.Instance).ProcessQueueAsync();
        }

        Assert.Equal(1, saved);
        using var assertions = db.NewContext();
        Assert.Single(await assertions.Observations.ToListAsync());
        var item = Assert.Single(await assertions.IngestionQueue.Where(i => i.Source == "ebird").ToListAsync());
        Assert.Equal((IngestionStatus.Completed, 1), (item.Status, item.AttemptCount));
    }

    private static async Task<bool> HasWorkLeftAsync(TestDatabase db, string source)
    {
        using var context = db.NewContext();
        var config = TestConfiguration.Empty();
        return source == "ebird"
            ? await new QueueSeederService(context, NullLogger<QueueSeederService>.Instance, config).CheckQueueCompletedAsync()
            : await new INatQueueSeederService(context, NullLogger<INatQueueSeederService>.Instance, config).CheckQueueCompletedAsync();
    }

    private static async Task AddAsync(TestDatabase db, string source, params (string Region, IngestionStatus Status, int Attempts)[] items)
    {
        using var context = db.NewContext();
        foreach (var (region, status, attempts) in items)
        {
            context.IngestionQueue.Add(new IngestionQueue { RegionCode = region, Source = source, Status = status, AttemptCount = attempts });
        }
        await context.SaveChangesAsync();
    }
}
