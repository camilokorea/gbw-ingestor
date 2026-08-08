using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Flickr.Matcher.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eBird.Ingestor.Tests.Flickr;

/// <summary>
/// The matcher writes into <c>SpeciesPhotos</c>, whose primary key is
/// (<c>SpeciesId</c>, <c>FlickrPhotoId</c>). Re-inserting a pair it already wrote would take the
/// whole unattended run down with a duplicate-key error, so idempotency across runs is the
/// property that matters most here.
/// </summary>
public class FlickrMatcherServiceTests
{
    [Fact]
    public async Task LinksAPhotoWhoseTitleNamesTheSpecies()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus")],
            photos: [("Keel-billed Toucan (Ramphastos sulfuratus)", null)]);

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        var link = Assert.Single(await assertions.SpeciesPhotos.ToListAsync());
        Assert.Equal(1, link.SpeciesId);
    }

    [Fact]
    public async Task LinksAPhotoWhoseTagsNameTheSpecies()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus")],
            photos: [("Toucan at dawn", "costarica birding ramphastos sulfuratus")]);

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.SpeciesPhotos.ToListAsync());
    }

    [Fact]
    public async Task LeavesAPhotoUnlinkedWhenNoSpeciesIsNamed()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus")],
            photos: [("Sunset over Arenal", "landscape volcano")]);

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        Assert.Empty(await assertions.SpeciesPhotos.ToListAsync());
    }

    [Fact]
    public async Task LinksOnePhotoToEverySpeciesNamedInIt()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus"), ("resque1", "Pharomachrus mocinno")],
            photos: [("Ramphastos sulfuratus and Pharomachrus mocinno in one frame", null)]);

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        Assert.Equal(2, await assertions.SpeciesPhotos.CountAsync());
    }

    [Fact]
    public async Task IsIdempotent_ASecondRunInsertsNothingAndDoesNotThrow()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus")],
            photos: [("Ramphastos sulfuratus", null)]);

        await RunMatcherAsync(db);
        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.SpeciesPhotos.ToListAsync());
    }

    [Fact]
    public async Task LinksAtMostOneRowPerSpeciesAndPhotoWhenNamesAreDuplicated()
    {
        using var db = TestDatabase.Create();
        // The same binomial under two rows — eBird's and iNat's. Only the first may be linked;
        // linking both would still be two distinct keys, but the site would show the photo twice.
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus"), ("inat-12345", "Ramphastos sulfuratus")],
            photos: [("Ramphastos sulfuratus", null)]);

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        var link = Assert.Single(await assertions.SpeciesPhotos.ToListAsync());
        Assert.Equal(1, link.SpeciesId);
    }

    [Fact]
    public async Task Documents_APhotoAlreadyLinkedToOneSpeciesIsNeverMatchedAgain()
    {
        using var db = TestDatabase.Create();
        await SeedAsync(db,
            species: [("rambto1", "Ramphastos sulfuratus"), ("resque1", "Pharomachrus mocinno")],
            photos: [("Ramphastos sulfuratus and Pharomachrus mocinno in one frame", null)]);

        // Candidate photos are filtered to those with no species link at all, so a photo that
        // picked up one link in an earlier run can never gain a second — for instance after a new
        // species is added to the taxonomy.
        using (var arrange = db.NewContext())
        {
            arrange.SpeciesPhotos.Add(new SpeciesPhoto { SpeciesId = 1, FlickrPhotoId = 1 });
            await arrange.SaveChangesAsync();
        }

        await RunMatcherAsync(db);

        using var assertions = db.NewContext();
        Assert.Single(await assertions.SpeciesPhotos.ToListAsync());
    }

    private static async Task RunMatcherAsync(TestDatabase db)
    {
        using var context = db.NewContext();
        var service = new FlickrMatcherService(context, NullLogger<FlickrMatcherService>.Instance);
        await service.MatchPhotosToSpeciesAsync();
    }

    private static async Task SeedAsync(
        TestDatabase db,
        (string Code, string ScientificName)[] species,
        (string Title, string? Tags)[] photos)
    {
        using var context = db.NewContext();

        foreach (var (code, scientificName) in species)
        {
            context.Species.Add(new Species { SpeciesCode = code, ScientificName = scientificName });
        }

        var photoId = 1;
        foreach (var (title, tags) in photos)
        {
            context.FlickrPhotos.Add(new FlickrPhoto { PhotoId = photoId++, Title = title, Tags = tags });
        }

        await context.SaveChangesAsync();
    }
}
