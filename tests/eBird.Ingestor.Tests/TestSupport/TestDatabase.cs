using eBird.Ingestor.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace eBird.Ingestor.Tests.TestSupport;

/// <summary>
/// A throwaway SQLite database held in memory for the lifetime of one test.
///
/// SQLite rather than the EF in-memory provider on purpose: the workers' dedup logic leans on
/// unique indexes (<c>SoundSignature.XenoCantoId</c>, <c>SpeciesPhoto</c>'s composite key,
/// <c>Species.SpeciesCode</c>), and the in-memory provider enforces none of them — the exact
/// class of bug these tests exist to catch would pass silently. SQLite also needs no Docker,
/// so the suite runs unchanged in CI.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EbirdIngestorDbContext> _options;
    private readonly List<EbirdIngestorDbContext> _contexts = new();

    private TestDatabase(SqliteConnection connection, DbContextOptions<EbirdIngestorDbContext> options)
    {
        _connection = connection;
        _options = options;
    }

    public static TestDatabase Create()
    {
        // The database lives only as long as the connection stays open, so this instance owns it.
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<EbirdIngestorDbContext>()
            .UseSqlite(connection)
            .Options;

        var database = new TestDatabase(connection, options);
        database.NewContext().Database.EnsureCreated();
        return database;
    }

    /// <summary>
    /// A fresh context over the same database. Use one context to arrange and another to assert
    /// so results come from the database rather than from the change tracker.
    /// </summary>
    public EbirdIngestorDbContext NewContext()
    {
        var context = new EbirdIngestorDbContext(_options);
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _connection.Dispose();
    }
}
