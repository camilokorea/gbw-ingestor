using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Ingestion;

/// <summary>
/// The iNat ingestor shares a schema with the eBird one and bridges the two taxonomies on
/// scientific name, so its get-or-create paths carry the extra risk of forking a species that
/// eBird already created. It also unpacks GeoJSON, where a longitude/latitude swap would put
/// every bird in the wrong place without failing anything.
/// </summary>
public class INatIngestionServiceTests
{
    private static readonly int CostaRicaPlaceId = INatCountryPlaceIds.GetPlaceId("CR")!.Value;

    [Fact]
    public async Task IngestsAnObservationWithItsCountryLocationAndSpecies()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var observation = Assert.Single(await assertions.Observations.ToListAsync());
        Assert.Equal("inat-555", observation.LocationId);
        Assert.Equal("inat", observation.Source);
        Assert.Equal(new DateTime(2026, 4, 21), observation.ObservationDate);

        var species = Assert.Single(await assertions.Species.ToListAsync());
        Assert.Equal("Ramphastos sulfuratus", species.ScientificName);
        Assert.Equal("inat-18762", species.SpeciesCode);
        Assert.Equal(18762, species.INatTaxonId);
        Assert.Equal(1, species.ObservationCount);

        Assert.Equal("CR", Assert.Single(await assertions.Countries.ToListAsync()).Code);
    }

    [Fact]
    public async Task ReadsGeoJsonCoordinatesAsLongitudeThenLatitude()
    {
        using var db = TestDatabase.Create();
        // GeoJSON is [lng, lat]; Costa Rica is north of the equator and west of Greenwich, so a
        // swap here would be unmistakable — and completely silent.
        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus", lng: -84.0, lat: 10.43));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var location = Assert.Single(await assertions.Locations.ToListAsync());
        Assert.Equal(10.43, location.Latitude);
        Assert.Equal(-84.0, location.Longitude);
    }

    [Fact]
    public async Task DoesNotReinsertAnObservationOnASecondRun()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);
        await RequeueAsync(db);
        await RunAsync(db, api);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.Observations.ToListAsync());
        Assert.Single(await assertions.Locations.ToListAsync());
        // The cached counter must not be double-counted by the re-run either.
        Assert.Equal(1, Assert.Single(await assertions.Species.ToListAsync()).ObservationCount);
    }

    [Fact]
    public async Task BridgesOntoAnExistingSpeciesWithTheSameScientificName()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            // Already ingested from eBird. iNat must attach to this row, not fork a second one.
            arrange.Species.Add(new Species { SpeciesCode = "rambto1", ScientificName = "Ramphastos sulfuratus" });
            await arrange.SaveChangesAsync();
        }

        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var species = Assert.Single(await assertions.Species.ToListAsync());
        Assert.Equal("rambto1", species.SpeciesCode);
        Assert.Equal(18762, species.INatTaxonId);
    }

    [Fact]
    public async Task CreatesOneSpeciesRowForRepeatedObservationsInOneBatch()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(
            Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"),
            Observation(id: 556, taxonId: 18762, "Ramphastos sulfuratus"));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var species = Assert.Single(await assertions.Species.ToListAsync());
        Assert.Equal(2, species.ObservationCount);
        Assert.Equal(2, await assertions.Observations.CountAsync());
    }

    [Fact]
    public async Task FallsBackToAncestorsForFamilyAndOrderWhenTheTaxonOmitsThem()
    {
        using var db = TestDatabase.Create();
        var observation = Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus");
        observation.Taxon.Family = null;
        observation.Taxon.Order = null;
        observation.Identifications =
        [
            new INatIdentificationDto
            {
                Taxon = new INatTaxonDetailDto
                {
                    Ancestors =
                    [
                        new INatAncestorDto { Rank = "class", Name = "Aves" },
                        new INatAncestorDto { Rank = "order", Name = "Piciformes" },
                        new INatAncestorDto { Rank = "family", Name = "Ramphastidae" }
                    ]
                }
            }
        ];

        await QueueAsync(db, "CR");
        await RunAsync(db, ApiWith(observation));

        using var assertions = db.NewContext();
        var species = Assert.Single(await assertions.Species.ToListAsync());
        Assert.Equal("Piciformes", species.Order);
        Assert.Equal("Ramphastidae", species.FamilySciName);
    }

    [Fact]
    public async Task NamesTheLocationFromPlaceGuessAndFallsBackWhenItIsBlank()
    {
        using var db = TestDatabase.Create();
        var named = Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus");
        named.PlaceGuess = "La Selva, Sarapiquí";
        var unnamed = Observation(id: 556, taxonId: 18762, "Ramphastos sulfuratus");
        unnamed.PlaceGuess = "   ";

        await QueueAsync(db, "CR");
        await RunAsync(db, ApiWith(named, unnamed));

        using var assertions = db.NewContext();
        Assert.Equal("La Selva, Sarapiquí", (await assertions.Locations.FindAsync("inat-555"))!.Name);
        Assert.Equal("iNaturalist Location", (await assertions.Locations.FindAsync("inat-556"))!.Name);
    }

    [Fact]
    public async Task SkipsACountryThatHasNoINatPlaceIdWithoutCallingTheApi()
    {
        using var db = TestDatabase.Create();
        var api = new FakeINatApiClient();
        await QueueAsync(db, "ZZ");

        await RunAsync(db, api);

        Assert.Empty(api.PagesRequested);
        using var assertions = db.NewContext();
        // Completed rather than Failed: an unmapped country is permanent, not transient, so
        // retrying it ten times would be wasted work.
        Assert.Equal(IngestionStatus.Completed, Assert.Single(await assertions.IngestionQueue.ToListAsync()).Status);
    }

    [Fact]
    public async Task StopsPagingAsSoonAsAPageComesBackShort()
    {
        using var db = TestDatabase.Create();
        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"));
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        Assert.Equal(new[] { 1 }, api.PagesRequested.Select(p => p.Page).ToArray());
    }

    [Fact]
    public async Task StopsAtTheConfiguredPageLimitWhenEveryPageIsFull()
    {
        using var db = TestDatabase.Create();
        var api = new FakeINatApiClient();
        api.ObservationsByPage[(CostaRicaPlaceId, 1)] = FullPage(1);
        api.ObservationsByPage[(CostaRicaPlaceId, 2)] = FullPage(2);
        api.ObservationsByPage[(CostaRicaPlaceId, 3)] = FullPage(3);
        await QueueAsync(db, "CR");

        await RunAsync(db, api, maxPagesPerCountry: 2);

        Assert.Equal(new[] { 1, 2 }, api.PagesRequested.Select(p => p.Page).ToArray());
        using var assertions = db.NewContext();
        Assert.Equal(400, await assertions.Observations.CountAsync());
    }

    [Fact]
    public async Task MarksTheRegionFailedAndCountsTheAttemptWhenTheApiThrows()
    {
        using var db = TestDatabase.Create();
        var api = new FakeINatApiClient { ObservationsFailure = new HttpRequestException("iNat is down") };
        await QueueAsync(db, "CR");

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        var item = Assert.Single(await assertions.IngestionQueue.ToListAsync());
        Assert.Equal(IngestionStatus.Failed, item.Status);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task IgnoresQueueItemsOwnedByTheEBirdIngestor()
    {
        using var db = TestDatabase.Create();
        using (var arrange = db.NewContext())
        {
            arrange.IngestionQueue.Add(new IngestionQueue { RegionCode = "CR", Source = "ebird", Status = IngestionStatus.Queued });
            await arrange.SaveChangesAsync();
        }

        var api = ApiWith(Observation(id: 555, taxonId: 18762, "Ramphastos sulfuratus"));
        await RunAsync(db, api);

        Assert.Empty(api.PagesRequested);
    }

    private static async Task RunAsync(TestDatabase db, FakeINatApiClient api, int maxPagesPerCountry = 5)
    {
        using var context = db.NewContext();
        var configuration = TestConfiguration.With(
            ("iNatIngestor:DaysAgo", "180"),
            ("iNatIngestor:MaxPagesPerCountry", maxPagesPerCountry.ToString()));

        var service = new INatIngestionService(api, context, NullLogger<INatIngestionService>.Instance, configuration);
        await service.ProcessQueueAsync();
    }

    private static FakeINatApiClient ApiWith(params INatObservationDto[] observations)
    {
        var api = new FakeINatApiClient();
        api.ObservationsByPage[(CostaRicaPlaceId, 1)] = observations.ToList();
        return api;
    }

    /// <summary>A page of exactly 200 results — the size that makes the ingestor ask for another.</summary>
    private static List<INatObservationDto> FullPage(int page) =>
        Enumerable.Range(0, 200)
            .Select(i => Observation(id: page * 1000 + i, taxonId: 18762, "Ramphastos sulfuratus"))
            .ToList();

    private static INatObservationDto Observation(
        long id,
        int taxonId,
        string scientificName,
        double lng = -84.0,
        double lat = 10.43) => new()
    {
        Id = id,
        ObservedOn = "2026-04-21",
        NumIdentificationAgreements = 1,
        PlaceGuess = "La Selva, Costa Rica",
        Taxon = new INatTaxonDto
        {
            Id = taxonId,
            Name = scientificName,
            PreferredCommonName = "Keel-billed Toucan",
            IconicTaxonName = "Aves",
            Family = "Ramphastidae",
            Order = "Piciformes"
        },
        Geojson = new INatGeoJsonDto { Coordinates = [lng, lat] }
    };

    private static async Task QueueAsync(TestDatabase db, params string[] regionCodes)
    {
        using var context = db.NewContext();
        foreach (var regionCode in regionCodes)
        {
            context.IngestionQueue.Add(new IngestionQueue
            {
                RegionCode = regionCode,
                Source = "inat",
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
