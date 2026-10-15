using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    DbSet<SpeciesTranslation> SpeciesTranslations { get; }
    DbSet<FlickrPhoto> FlickrPhotos { get; }
    DbSet<FlickrPhotoLocation> FlickrPhotoLocations { get; }
    DbSet<SpeciesPhoto> SpeciesPhotos { get; }
    DbSet<SpeciesColor> SpeciesColors { get; }
    DbSet<SyncState> SyncStates { get; }
    DbSet<INatPhoto> INatPhotos { get; }
    DbSet<SpeciesINatPhoto> SpeciesINatPhotos { get; }
    DbSet<CommonsPhoto> CommonsPhotos { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exposes the EF Core change tracker so long-running batch workers can release tracked
    /// entities between saves (<c>ChangeTracker.Clear()</c>) to keep memory flat.
    /// Satisfied for free by <see cref="DbContext"/>.
    /// </summary>
    ChangeTracker ChangeTracker { get; }
}