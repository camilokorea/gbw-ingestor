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
