using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;

namespace eBird.Ingestor.Application.Contracts.Persistence;

public interface IApplicationDbContext
{
    DbSet<Observation> Observations { get; }
    DbSet<Species> Species { get; }
    DbSet<Location> Locations { get; }
    DbSet<Country> Countries { get; }
    DbSet<State> States { get; }
    DbSet<IngestionQueue> IngestionQueue { get; }
    DbSet<Sound.Ingestor.Domain.SoundSignature> SoundSignatures { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}