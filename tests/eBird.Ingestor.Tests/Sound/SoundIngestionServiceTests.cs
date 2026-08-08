using eBird.Ingestor.Domain;
using eBird.Ingestor.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sound.Ingestor.Application.Contracts.Dtos;
using Sound.Ingestor.Application.Services;
using Xunit;

namespace eBird.Ingestor.Tests.Sound;

/// <summary>
/// The sound ingestor walks the whole species table against Xeno-Canto in a run that lasts hours,
/// so its two hard-won properties are batching (partial progress survives a crash) and dedup: one
/// recording is regularly returned for several species, and <c>SoundSignature.XenoCantoId</c> is
/// uniquely indexed, so the second insert would take down the run.
/// </summary>
public class SoundIngestionServiceTests
{
    [Fact]
    public async Task SavesTheRecordingsItFindsForASpecies()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");

        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] = Response(Recording("1001"), Recording("1002"));

        var saved = await RunAsync(db, api);

        Assert.Equal(2, saved);
        using var assertions = db.NewContext();
        Assert.Equal(new[] { 1001, 1002 },
            await assertions.SoundSignatures.OrderBy(s => s.XenoCantoId).Select(s => s.XenoCantoId).ToArrayAsync());
    }

    [Fact]
    public async Task InsertsARecordingOnceWhenTwoSpeciesShareIt()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus", "Ramphastos ambiguus");

        // Xeno-Canto returns the same recording under both names. The unique index on
        // XenoCantoId means the second insert has to be suppressed before SaveChanges, not after.
        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] = Response(Recording("1001"));
        api.RecordingsByScientificName["Ramphastos ambiguus"] = Response(Recording("1001"));

        var saved = await RunAsync(db, api);

        Assert.Equal(1, saved);
        using var assertions = db.NewContext();
        Assert.Single(await assertions.SoundSignatures.ToListAsync());
    }

    [Fact]
    public async Task DoesNotReinsertARecordingItAlreadyStored()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");

        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] = Response(Recording("1001"));

        await RunAsync(db, api);
        var savedOnSecondRun = await RunAsync(db, api);

        Assert.Equal(0, savedOnSecondRun);
        using var assertions = db.NewContext();
        Assert.Single(await assertions.SoundSignatures.ToListAsync());
    }

    [Fact]
    public async Task PersistsInBatchesSoAHaltedRunKeepsItsProgress()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");

        // 1,200 recordings: two full batches of 500 plus a 200-row remainder that only the final
        // flush picks up. Anything dropped here is silent data loss.
        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] =
            Response(Enumerable.Range(1, 1200).Select(i => Recording(i.ToString())).ToArray());

        var saved = await RunAsync(db, api);

        Assert.Equal(1200, saved);
        using var assertions = db.NewContext();
        Assert.Equal(1200, await assertions.SoundSignatures.CountAsync());
    }

    [Fact]
    public async Task SkipsRecordingsWithANonNumericId()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");

        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] = Response(Recording("XC1001"), Recording("1002"));

        var saved = await RunAsync(db, api);

        Assert.Equal(1, saved);
        using var assertions = db.NewContext();
        Assert.Equal(1002, Assert.Single(await assertions.SoundSignatures.ToListAsync()).XenoCantoId);
    }

    [Theory]
    // Xeno-Canto historically returned protocol-relative URLs; v3 returns either form. Prefixing
    // an already-absolute URL would produce "https:https://…" and a dead audio player.
    [InlineData("//xeno-canto.org/sounds/uploaded/XC1001.mp3", "https://xeno-canto.org/sounds/uploaded/XC1001.mp3")]
    [InlineData("https://xeno-canto.org/sounds/uploaded/XC1001.mp3", "https://xeno-canto.org/sounds/uploaded/XC1001.mp3")]
    [InlineData("http://xeno-canto.org/sounds/uploaded/XC1001.mp3", "http://xeno-canto.org/sounds/uploaded/XC1001.mp3")]
    [InlineData("  //xeno-canto.org/XC1001.mp3  ", "https://xeno-canto.org/XC1001.mp3")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public async Task NormalizesAudioUrls(string? apiValue, string expected)
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus");

        var recording = Recording("1001");
        recording.AudioUrl = apiValue!;

        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Ramphastos sulfuratus"] = Response(recording);

        await RunAsync(db, api);

        using var assertions = db.NewContext();
        Assert.Equal(expected, Assert.Single(await assertions.SoundSignatures.ToListAsync()).AudioUrl);
    }

    [Fact]
    public async Task StopsEarlyWhenThereAreNoSpeciesToLookUp()
    {
        using var db = TestDatabase.Create();
        var api = new FakeXenoCantoApiClient();

        var saved = await RunAsync(db, api);

        Assert.Equal(0, saved);
        Assert.Empty(api.NamesRequested);
    }

    [Fact]
    public async Task CarriesOnWhenASpeciesHasNoRecordings()
    {
        using var db = TestDatabase.Create();
        await SeedSpeciesAsync(db, "Ramphastos sulfuratus", "Pharomachrus mocinno");

        var api = new FakeXenoCantoApiClient();
        api.RecordingsByScientificName["Pharomachrus mocinno"] = Response(Recording("1001"));

        var saved = await RunAsync(db, api);

        Assert.Equal(1, saved);
        Assert.Equal(
            new[] { "Pharomachrus mocinno", "Ramphastos sulfuratus" },
            api.NamesRequested.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    private static async Task<int> RunAsync(TestDatabase db, FakeXenoCantoApiClient api)
    {
        using var context = db.NewContext();
        var service = new SoundIngestionService(context, api, NullLogger<SoundIngestionService>.Instance);
        return await service.ProcessSoundsAsync();
    }

    private static XenoCantoResponseDto Response(params XenoCantoRecordingDto[] recordings) =>
        new() { Recordings = recordings.ToList() };

    private static XenoCantoRecordingDto Recording(string id) => new()
    {
        Id = id,
        Type = "song",
        Quality = "A",
        AudioUrl = $"//xeno-canto.org/sounds/uploaded/XC{id}.mp3",
        RecordistUrl = $"//xeno-canto.org/{id}",
        License = "//creativecommons.org/licenses/by-nc-sa/4.0/"
    };

    private static async Task SeedSpeciesAsync(TestDatabase db, params string[] scientificNames)
    {
        using var context = db.NewContext();
        var code = 1;
        foreach (var scientificName in scientificNames)
        {
            context.Species.Add(new Species { SpeciesCode = $"sp{code++}", ScientificName = scientificName });
        }

        await context.SaveChangesAsync();
    }
}
