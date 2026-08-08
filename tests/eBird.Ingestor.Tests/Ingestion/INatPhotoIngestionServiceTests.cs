using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Ingestion;

/// <summary>
/// Backfills photos for species that have none. The same iNat photo is routinely returned for
/// more than one species, and <c>INatPhoto.PhotoId</c> is uniquely indexed, so the reuse check is
/// the difference between a clean run and a duplicate-key crash partway through.
/// </summary>
public class INatPhotoIngestionServiceTests
{
    [Fact]
    public async Task AddsPhotosAndLinksThemToTheSpecies()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, ("rambto1", "Ramphastos sulfuratus", 18762L));

        var photos = new FakeINatPhotoService();
        photos.PhotosByTaxonId[18762] = [Photo(900001), Photo(900002)];

        await RunAsync(db, photos);

        using var assertions = db.NewContext();
        Assert.Equal(2, await assertions.INatPhotos.CountAsync());
        Assert.Equal(2, await assertions.SpeciesINatPhotos.CountAsync());
    }

    [Fact]
    public async Task ReusesAnExistingPhotoRowWhenTwoSpeciesShareAPhoto()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db,
            ("rambto1", "Ramphastos sulfuratus", 18762L),
            ("resque1", "Pharomachrus mocinno", 18763L));

        var photos = new FakeINatPhotoService();
        photos.PhotosByTaxonId[18762] = [Photo(900001)];
        photos.PhotosByTaxonId[18763] = [Photo(900001)];

        await RunAsync(db, photos);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.INatPhotos.ToListAsync());
        Assert.Equal(2, await assertions.SpeciesINatPhotos.CountAsync());
    }

    [Fact]
    public async Task CachesTheResolvedTaxonIdBackOntoTheSpecies()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, ("rambto1", "Ramphastos sulfuratus", null));

        var photos = new FakeINatPhotoService();
        photos.TaxonIdsByScientificName["Ramphastos sulfuratus"] = 18762L;
        photos.PhotosByTaxonId[18762] = [Photo(900001)];

        await RunAsync(db, photos);

        using var assertions = db.NewContext();
        Assert.Equal(18762L, Assert.Single(await assertions.Species.ToListAsync()).INatTaxonId);
        Assert.Equal(new[] { "Ramphastos sulfuratus" }, photos.TaxonLookups.ToArray());
    }

    [Fact]
    public async Task SkipsASpeciesWhoseTaxonCannotBeResolved()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, ("rambto1", "Ramphastos sulfuratus", null));

        var photos = new FakeINatPhotoService();

        await RunAsync(db, photos);

        Assert.Empty(photos.PhotoLookups);
        using var assertions = db.NewContext();
        Assert.Empty(await assertions.INatPhotos.ToListAsync());
        Assert.Null(Assert.Single(await assertions.Species.ToListAsync()).INatTaxonId);
    }

    [Fact]
    public async Task LeavesAloneASpeciesThatAlreadyHasAFlickrPhoto()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, ("rambto1", "Ramphastos sulfuratus", 18762L));
        using (var arrange = db.NewContext())
        {
            arrange.FlickrPhotos.Add(new FlickrPhoto { PhotoId = 7, Title = "Ramphastos sulfuratus" });
            await arrange.SaveChangesAsync();
            arrange.SpeciesPhotos.Add(new SpeciesPhoto { SpeciesId = 1, FlickrPhotoId = 1 });
            await arrange.SaveChangesAsync();
        }

        var photos = new FakeINatPhotoService();
        photos.PhotosByTaxonId[18762] = [Photo(900001)];

        await RunAsync(db, photos);

        Assert.Empty(photos.PhotoLookups);
    }

    [Fact]
    public async Task ProcessesTheMostObservedSpeciesFirst()
    {
        using var db = TestDatabase.Create();
        // Only 100 species are handled per run, so ordering decides which birds users actually
        // see a photo for. No photos are returned here, which keeps the worker's polite delays out.
        await SeedSpeciesAsync(db,
            ("rare1", "Philodice bryantae", 6001L),
            ("common1", "Ramphastos sulfuratus", 6002L),
            ("mid1", "Pharomachrus mocinno", 6003L));
        await SeedObservationsAsync(db, speciesId: 1, count: 1);
        await SeedObservationsAsync(db, speciesId: 2, count: 5);
        await SeedObservationsAsync(db, speciesId: 3, count: 3);

        var photos = new FakeINatPhotoService();

        await RunAsync(db, photos);

        Assert.Equal(new[] { 6002L, 6003L, 6001L }, photos.PhotoLookups.ToArray());
    }

    private static async Task RunAsync(TestDatabase db, FakeINatPhotoService photos)
    {
        using var context = db.NewContext();
        var service = new INatPhotoIngestionService(context, photos, NullLogger<INatPhotoIngestionService>.Instance);
        await service.ProcessSpeciesPhotosAsync();
    }

    private static INatPhoto Photo(long photoId) => new()
    {
        PhotoId = photoId,
        Attribution = "(c) A Photographer, CC-BY-NC",
        LicenseCode = "cc-by-nc",
        MediumUrl = $"https://inaturalist-open-data.s3.amazonaws.com/photos/{photoId}/medium.jpg",
        WebUrl = $"https://www.inaturalist.org/photos/{photoId}"
    };

    private static async Task SeedSpeciesAsync(
        TestDatabase db,
        params (string Code, string ScientificName, long? TaxonId)[] species)
    {
        using var context = db.NewContext();
        foreach (var (code, scientificName, taxonId) in species)
        {
            context.Species.Add(new Species
            {
                SpeciesCode = code,
                ScientificName = scientificName,
                INatTaxonId = taxonId
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedObservationsAsync(TestDatabase db, int speciesId, int count)
    {
        using var context = db.NewContext();

        if (await context.Countries.FindAsync("CR") is null)
        {
            context.Countries.Add(new Country { Code = "CR", Name = "Costa Rica" });
        }

        for (var i = 0; i < count; i++)
        {
            var locationId = $"L{speciesId}-{i}";
            context.Locations.Add(new Location { Id = locationId, Name = locationId, CountryCode = "CR" });
            context.Observations.Add(new Observation
            {
                SpeciesId = speciesId,
                LocationId = locationId,
                ObservationDate = new DateTime(2026, 4, 21),
                HowMany = 1
            });
        }

        await context.SaveChangesAsync();
    }
}
