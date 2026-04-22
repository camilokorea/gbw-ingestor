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
    DbSet<SpeciesTranslation> SpeciesTranslations { get; }
    DbSet<FlickrPhoto> FlickrPhotos { get; }
    DbSet<FlickrPhotoLocation> FlickrPhotoLocations { get; }
    DbSet<SpeciesPhoto> SpeciesPhotos { get; }
    DbSet<SpeciesColor> SpeciesColors { get; }
    DbSet<SyncState> SyncStates { get; }
    DbSet<INatPhoto> INatPhotos { get; }
    DbSet<SpeciesINatPhoto> SpeciesINatPhotos { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}