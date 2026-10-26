using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Ingestion;

/// <summary>
/// The historic backfill refills the four months the eBird queue was frozen (June to October 2026): ~32k calls over
/// hours, so it must resume after a stop, survive a region that keeps failing, never duplicate an observation, and
/// stay within eBird's limits by taking each site's state from the response instead of one lookup per site.
/// </summary>
public class HistoricBackfillTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 15), Day2 = new(2026, 8, 16);

    [Fact]
    public async Task Stores_each_days_observations_with_their_state_and_no_location_lookups()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricByRegionDay[("CR", Day1)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00")];
        api.HistoricByRegionDay[("CR", Day2)] = [Obs("resque1", "Pharomachrus mocinno", "L2", "2026-08-16 08:30")];

        var saved = await Backfill(db, api, ["CR"], Day1, Day2);

        Assert.Equal(2, saved);
        Assert.Empty(api.LocationsRequested);
        using var assertions = db.NewContext();
        Assert.Equal(2, await assertions.Observations.CountAsync());
        Assert.Equal("Alajuela", (await assertions.States.SingleAsync()).Name);
        Assert.All(await assertions.Locations.ToListAsync(), l => Assert.NotNull(l.StateId));
    }

    [Fact]
    public async Task A_second_run_skips_the_days_already_done()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricByRegionDay[("CR", Day1)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00")];

        await Backfill(db, api, ["CR"], Day1, Day1);
        var callsAfterFirst = api.HistoricRequested.Count;
        var saved = await Backfill(db, api, ["CR"], Day1, Day2);

        Assert.Equal(0, saved);
        Assert.Equal([("CR", Day2)], api.HistoricRequested.Skip(callsAfterFirst));
    }

    [Fact]
    public async Task A_failing_region_is_retried_twice_then_left_out_without_stopping_the_others()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricFailingRegions.Add("US");
        api.HistoricByRegionDay[("CR", Day1)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00")];

        var saved = await Backfill(db, api, ["US", "CR"], Day1, Day1);

        Assert.Equal(1, saved);
        Assert.Equal(3, api.HistoricRequested.Count(r => r.Region == "US"));
        using var assertions = db.NewContext();
        Assert.True(await assertions.SyncStates.AnyAsync(s => s.Key == "ebird-historic:20260815"));
    }

    [Fact]
    public async Task Observations_already_stored_are_not_added_again()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        var same = Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00");
        api.HistoricByRegionDay[("CR", Day1)] = [same, same];

        var first = await Backfill(db, api, ["CR"], Day1, Day1);

        Assert.Equal(1, first);
        using var assertions = db.NewContext();
        Assert.Equal(1, await assertions.Observations.CountAsync());
    }

    [Fact]
    public async Task A_region_too_large_for_one_call_is_fetched_by_state_then_county_and_remembered()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricTooLarge.UnionWith(["US", "US-CA"]);
        api.Subregions["US"] = ["US-CA", "US-OR"];
        api.Subregions["US-CA"] = ["US-CA-001", "US-CA-003"];
        api.HistoricByRegionDay[("US-OR", Day1)] = [Obs("stejay", "Cyanocitta stelleri", "L1", "2026-08-15 07:00")];
        api.HistoricByRegionDay[("US-CA-001", Day1)] = [Obs("calqua", "Callipepla californica", "L2", "2026-08-15 08:00")];
        api.HistoricByRegionDay[("US-CA-003", Day2)] = [Obs("acowoo", "Melanerpes formicivorus", "L3", "2026-08-16 09:00")];

        var saved = await Backfill(db, api, ["US"], Day1, Day2);

        Assert.Equal(3, saved);
        // The 60-second refusals happen once: on day 2 the run goes straight to the counties and Oregon.
        Assert.Equal([("US-CA-001", Day2), ("US-CA-003", Day2), ("US-OR", Day2)], api.HistoricRequested.Where(r => r.Day == Day2));
        Assert.Equal(["US", "US-CA"], api.SubregionsRequested);
    }

    [Fact]
    public async Task Each_region_is_released_after_saving_and_a_second_day_still_finds_what_the_first_stored()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        // Same site and species on two days, plus a species new to the run seen twice on one day.
        api.HistoricByRegionDay[("CR", Day1)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00"),
                                                 Obs("newsp1", "Avis nova", "L1", "2026-08-15 07:00"),
                                                 Obs("newsp1", "Avis nova", "L1", "2026-08-15 07:00")];
        api.HistoricByRegionDay[("CR", Day2)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00"),
                                                 Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-16 07:00")];

        using var context = db.NewContext();
        var saved = await new IngestionService(api, context, NullLogger<IngestionService>.Instance)
            .BackfillHistoricAsync(["CR"], Day1, Day2);

        Assert.Equal(3, saved); // toucan day 1, new species once, toucan day 2; the repeat of day 1 is skipped
        Assert.Empty(context.ChangeTracker.Entries<eBird.Ingestor.Domain.Observation>());
        using var assertions = db.NewContext();
        Assert.Equal(3, await assertions.Observations.CountAsync());
        Assert.Equal(1, await assertions.Species.CountAsync(s => s.SpeciesCode == "newsp1"));
    }

    [Fact]
    public async Task The_run_records_its_range_the_split_regions_and_the_ones_left_out_and_ends_its_heartbeat()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricTooLarge.Add("AU");
        api.Subregions["AU"] = ["AU-NSW"];
        api.HistoricFailingRegions.Add("US");

        await Backfill(db, api, ["AU", "US"], Day1, Day1);

        using var assertions = db.NewContext();
        var states = await assertions.SyncStates.ToDictionaryAsync(s => s.Key, s => s.LastSyncValue);
        Assert.Equal(20260815, states["ebird-historic:from"]);
        Assert.Equal(20260815, states["ebird-historic:to"]);
        Assert.Equal(1, states["ebird-historic-split:AU"]);
        Assert.True(states.ContainsKey("ebird-historic-missing:20260815:US"));
        Assert.False(states.ContainsKey("ebird-historic:active")); // finished: the regular run may go again
    }

    [Fact]
    public async Task Missing_regions_are_retried_cleared_and_added_to_their_day()
    {
        using var db = TestDatabase.Create();
        var api = new FakeEbirdApiClient();
        api.HistoricFailingRegions.Add("US");
        api.HistoricByRegionDay[("CR", Day1)] = [Obs("rambto1", "Ramphastos sulfuratus", "L1", "2026-08-15 07:00")];
        api.HistoricByRegionDay[("US", Day1)] = [Obs("amerob", "Turdus migratorius", "L9", "2026-08-15 06:00")];
        await Backfill(db, api, ["US", "CR"], Day1, Day1);

        api.HistoricFailingRegions.Clear();
        using var context = db.NewContext();
        var recovered = await new IngestionService(api, context, NullLogger<IngestionService>.Instance).BackfillMissingAsync();

        Assert.Equal(1, recovered);
        using var assertions = db.NewContext();
        Assert.False(await assertions.SyncStates.AnyAsync(s => s.Key.StartsWith("ebird-historic-missing:")));
        Assert.Equal(2, (await assertions.SyncStates.SingleAsync(s => s.Key == "ebird-historic:20260815")).LastSyncValue);
    }

    [Fact]
    public async Task The_backfill_counts_as_running_only_while_its_heartbeat_is_fresh()
    {
        using var db = TestDatabase.Create();
        using var context = db.NewContext();
        var service = new IngestionService(new FakeEbirdApiClient(), context, NullLogger<IngestionService>.Instance);

        Assert.False(await service.IsHistoricBackfillActiveAsync(TimeSpan.FromHours(2)));
        context.SyncStates.Add(new eBird.Ingestor.Domain.SyncState { Key = "ebird-historic:active", UpdatedAt = DateTime.UtcNow.AddMinutes(-10) });
        await context.SaveChangesAsync();
        Assert.True(await service.IsHistoricBackfillActiveAsync(TimeSpan.FromHours(2)));
        Assert.False(await service.IsHistoricBackfillActiveAsync(TimeSpan.FromMinutes(5)));
    }

    private static async Task<int> Backfill(TestDatabase db, FakeEbirdApiClient api, string[] regions, DateOnly from, DateOnly to)
    {
        using var context = db.NewContext();
        return await new IngestionService(api, context, NullLogger<IngestionService>.Instance)
            .BackfillHistoricAsync(regions, from, to);
    }

    private static EbirdObservationDto Obs(string code, string sciName, string locId, string at) => new()
    {
        SpeciesCode = code, ComName = code, SciName = sciName, LocId = locId, LocName = locId, ObsDt = at,
        HowMany = 1, Lat = 10.0, Lng = -84.2, StateCode = "CR-A", StateName = "Alajuela",
    };
}
