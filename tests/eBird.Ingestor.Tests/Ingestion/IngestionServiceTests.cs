using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Ingestion;

/// <summary>
/// The eBird ingestor runs unattended and writes on every pass. The failure modes that have
/// actually bitten are all in the get-or-create paths — a second <c>Species</c> row for a code
/// that already exists, a duplicate <c>State</c> against its unique index, an observation
/// re-inserted on the next run — and none of them announce themselves.
/// </summary>
public class IngestionServiceTests
{
    [Fact]
    public async Task IngestsANewObservationWithItsCountryStateLocationAndSpecies()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(("CR", [Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15")]));
        api.LocationDetailsByLocId["L123"] = new EbirdLocationDetailsDto { StateCode = "CR-A", StateName = "Alajuela" };
        api.LocalizedNames[("rambto1", "es")] = "Tucán Pico Iris";
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var observation = Assert.Single(await assertions.Observations.ToListAsync());
        Assert.Equal("L123", observation.LocationId);
        Assert.Equal(new DateTime(2026, 4, 21, 7, 15, 0), observation.ObservationDate);
        Assert.Equal("ebird", observation.Source);

        var species = Assert.Single(await assertions.Species.Include(s => s.Translations).ToListAsync());
        Assert.Equal("Ramphastos sulfuratus", species.ScientificName);
        Assert.Equal(
            new[] { ("en", "Keel-billed Toucan"), ("es", "Tucán Pico Iris") },
            species.Translations.OrderBy(t => t.Locale).Select(t => (t.Locale, t.CommonName)).ToArray());

        Assert.Equal("CR", Assert.Single(await assertions.Countries.ToListAsync()).Code);
        Assert.Equal("CR-A", Assert.Single(await assertions.States.ToListAsync()).Code);
    }

    [Fact]
    public async Task DoesNotReinsertAnObservationOnASecondRun()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(("CR", [Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15")]));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);
        await RequeueAsync(db);
        await RunAsync(db, api);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.Observations.ToListAsync());
        Assert.Single(await assertions.Species.ToListAsync());
        Assert.Single(await assertions.Locations.ToListAsync());
    }

    [Fact]
    public async Task ReusesOneSpeciesRowForRepeatedSightingsInTheSameBatch()
    {
        using var db = TestDatabase.Create();
        // Same bird at two hides. The second sighting must find the species in the change tracker,
        // not create a second row — Species.SpeciesCode is uniquely indexed.
        var api = ApiWith(("CR",
        [
            Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15"),
            Observation("rambto1", "Ramphastos sulfuratus", "L456", "2026-04-21 09:40")
        ]));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.Species.ToListAsync());
        Assert.Equal(2, await assertions.Observations.CountAsync());
    }

    [Fact]
    public async Task ReusesOneStateRowForTwoLocationsInTheSameState()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(("CR",
        [
            Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15"),
            Observation("resque1", "Pharomachrus mocinno", "L456", "2026-04-21 09:40")
        ]));
        var alajuela = new EbirdLocationDetailsDto { StateCode = "CR-A", StateName = "Alajuela" };
        api.LocationDetailsByLocId["L123"] = alajuela;
        api.LocationDetailsByLocId["L456"] = alajuela;
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.States.ToListAsync());
        Assert.Equal(2, await assertions.Locations.CountAsync());
    }

    [Fact]
    public async Task DoesNotCallTheLocationApiForALocationItAlreadyKnows()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            arrange.Countries.Add(new Country { Code = "CR", Name = "Costa Rica" });
            arrange.Locations.Add(new Location { Id = "L123", Name = "La Selva", CountryCode = "CR" });
            await arrange.SaveChangesAsync();
        }

        var api = ApiWith(("CR", [Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15")]));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        Assert.Empty(api.LocationsRequested);
        using var assertions = db.NewContext();
        Assert.Single(await assertions.Locations.ToListAsync());
    }

    [Fact]
    public async Task MarksTheRegionCompletedWhenEBirdReturnsNothing()
    {
        using var db = TestDatabase.Create();
        await QueueAsync(db, "CR");

        await RunAsync(db, new FakeEbirdApiClient());

        using var assertions = db.NewContext();
        var item = Assert.Single(await assertions.IngestionQueue.ToListAsync());
        Assert.Equal(IngestionStatus.Completed, item.Status);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task MarksTheRegionFailedAndCountsTheAttemptWhenTheApiThrows()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient { ObservationsFailure = new HttpRequestException("eBird is down") };
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var item = Assert.Single(await assertions.IngestionQueue.ToListAsync());
        Assert.Equal(IngestionStatus.Failed, item.Status);
        Assert.Equal(1, item.AttemptCount);
        Assert.NotNull(item.LastAttemptUtc);
    }

    [Fact]
    public async Task KeepsProcessingTheRestOfTheBatchAfterOneRegionFails()
    {
        using var db = TestDatabase.Create();
        // The fake fails every region, so the assertion is that both were attempted and neither
        // was left stuck in Processing — a region stuck in Processing is never retried.
        var api = new FakeEbirdApiClient { ObservationsFailure = new HttpRequestException("eBird is down") };
        await QueueAsync(db, "CR", "PA");

        await RunAsync(db, api);

        Assert.Equal(new[] { "CR", "PA" }, api.RegionsRequested.OrderBy(r => r).ToArray());
        using var assertions = db.NewContext();
        Assert.All(await assertions.IngestionQueue.ToListAsync(), i => Assert.Equal(IngestionStatus.Failed, i.Status));
    }

    [Fact]
    public async Task IgnoresQueueItemsOwnedByAnotherIngestor()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            arrange.IngestionQueue.Add(new IngestionQueue { RegionCode = "CR", Source = "inat", Status = IngestionStatus.Queued });
            await arrange.SaveChangesAsync();
        }

        var api = ApiWith(("CR", [Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15")]));
        await RunAsync(db, api);

        Assert.Empty(api.RegionsRequested);
        using var assertions = db.NewContext();
        Assert.Equal(IngestionStatus.Queued, Assert.Single(await assertions.IngestionQueue.ToListAsync()).Status);
    }

    [Fact]
    public async Task GivesUpOnARegionAfterTenFailedAttempts()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            arrange.IngestionQueue.Add(new IngestionQueue
            {
                RegionCode = "CR",
                Source = "ebird",
                Status = IngestionStatus.Failed,
                AttemptCount = 10
            });
            await arrange.SaveChangesAsync();
        }

        var api = new FakeEbirdApiClient();
        await RunAsync(db, api);

        Assert.Empty(api.RegionsRequested);
        using var assertions = db.NewContext();
        Assert.Equal(10, Assert.Single(await assertions.IngestionQueue.ToListAsync()).AttemptCount);
    }

    [Fact]
    public async Task RetriesARegionThatFailedFewerThanTenTimes()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            arrange.IngestionQueue.Add(new IngestionQueue
            {
                RegionCode = "CR",
                Source = "ebird",
                Status = IngestionStatus.Failed,
                AttemptCount = 9
            });
            await arrange.SaveChangesAsync();
        }

        var api = ApiWith(("CR", [Observation("rambto1", "Ramphastos sulfuratus", "L123", "2026-04-21 07:15")]));
        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var item = Assert.Single(await assertions.IngestionQueue.ToListAsync());
        Assert.Equal(IngestionStatus.Completed, item.Status);
        Assert.Equal(10, item.AttemptCount);
    }

    private static async Task RunAsync(TestDatabase db, FakeEbirdApiClient api)
    {
        using var context = db.NewContext();
        var service = new IngestionService(api, context, NullLogger<IngestionService>.Instance);
        await service.ProcessQueueAsync();
    }

    private static FakeEbirdApiClient ApiWith(params (string Region, List<EbirdObservationDto> Observations)[] regions)
    {
        var api = new FakeEbirdApiClient();
        foreach (var (region, observations) in regions)
        {
            api.ObservationsByRegion[region] = observations;
        }

        return api;
    }

    private static EbirdObservationDto Observation(string speciesCode, string sciName, string locId, string observedAt) => new()
    {
        SpeciesCode = speciesCode,
        ComName = speciesCode == "rambto1" ? "Keel-billed Toucan" : "Resplendent Quetzal",
        SciName = sciName,
        LocId = locId,
        LocName = $"Location {locId}",
        ObsDt = observedAt,
        HowMany = 2,
        Lat = 10.43,
        Lng = -84.00
    };

    private static async Task QueueAsync(TestDatabase db, params string[] regionCodes)
    {
        using var context = db.NewContext();
        foreach (var regionCode in regionCodes)
        {
            context.IngestionQueue.Add(new IngestionQueue
            {
                RegionCode = regionCode,
                Source = "ebird",
                Status = IngestionStatus.Queued
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task RequeueAsync(TestDatabase db)
    {
        using var context = db.NewContext();
        foreach (var item in await context.IngestionQueue.ToListAsync())
        {
            item.Status = IngestionStatus.Queued;
        }

        await context.SaveChangesAsync();
    }
}
