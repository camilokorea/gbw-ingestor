using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;
using Sound.Ingestor.Domain;

namespace eBird.Ingestor.Infrastructure.Data;

public class EbirdIngestorDbContext : DbContext, IApplicationDbContext
{
    public EbirdIngestorDbContext(DbContextOptions<EbirdIngestorDbContext> options) : base(options)
    {
    }

    public DbSet<Observation> Observations { get; set; }
    public DbSet<Species> Species { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<Country> Countries { get; set; }
    public DbSet<State> States { get; set; }
    public DbSet<IngestionQueue> IngestionQueue { get; set; }
    public DbSet<SoundSignature> SoundSignatures { get; set; }
    public DbSet<SpeciesTranslation> SpeciesTranslations { get; set; }

    // Flickr workers
    public DbSet<FlickrPhoto> FlickrPhotos { get; set; }
    public DbSet<FlickrPhotoLocation> FlickrPhotoLocations { get; set; }
    public DbSet<SpeciesPhoto> SpeciesPhotos { get; set; }

    // Color data
    public DbSet<SpeciesColor> SpeciesColors { get; set; }

    // Sync state
    public DbSet<SyncState> SyncStates { get; set; }

    // iNaturalist photos
    public DbSet<INatPhoto> INatPhotos { get; set; }
    public DbSet<SpeciesINatPhoto> SpeciesINatPhotos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Country>(entity =>
        {
            entity.HasKey(c => c.Code);
            entity.Property(c => c.Code).ValueGeneratedNever();
        });

        modelBuilder.Entity<State>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.Code).IsUnique();

            entity.HasOne(s => s.Country)
                .WithMany(c => c.States)
                .HasForeignKey(s => s.CountryCode);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(l => l.Id).ValueGeneratedNever();

            entity.HasOne(l => l.Country)
                .WithMany(c => c.Locations)
                .HasForeignKey(l => l.CountryCode);

            entity.HasOne(l => l.State)
                .WithMany(s => s.Locations)
                .HasForeignKey(l => l.StateId)
                .IsRequired(false);
        });

        modelBuilder.Entity<Species>(entity =>
        {
            entity.HasIndex(s => s.SpeciesCode).IsUnique();
        });

        modelBuilder.Entity<SpeciesTranslation>(entity =>
        {
            // Un species solo puede tener una traducción por idioma.
            entity.HasIndex(st => new { st.SpeciesId, st.Locale }).IsUnique();

            entity.HasOne(st => st.Species)
                .WithMany(s => s.Translations)
                .HasForeignKey(st => st.SpeciesId);
        });

        modelBuilder.Entity<Observation>(entity =>
        {
            entity.HasIndex(o => o.ObservationDate);

            entity.HasOne(o => o.Location)
                .WithMany(l => l.Observations)
                .HasForeignKey(o => o.LocationId);
        });

        modelBuilder.Entity<IngestionQueue>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>();

            entity.HasIndex(e => e.RegionCode);
            entity.HasIndex(e => e.Source);
        });

        modelBuilder.Entity<SoundSignature>(entity =>
        {
            entity.HasIndex(s => s.XenoCantoId).IsUnique();

            entity.HasOne(s => s.Species)
                .WithMany()
                .HasForeignKey(s => s.SpeciesId);
        });

        // ── Flickr entities ────────────────────────────────────────────────

        modelBuilder.Entity<FlickrPhoto>(entity =>
        {
            entity.HasIndex(p => p.PhotoId).IsUnique();
        });

        modelBuilder.Entity<FlickrPhotoLocation>(entity =>
        {
            entity.HasOne(l => l.Photo)
                .WithOne(p => p.Location)
                .HasForeignKey<FlickrPhotoLocation>(l => l.FlickrPhotoId);
        });

        modelBuilder.Entity<SpeciesPhoto>(entity =>
        {
            entity.HasKey(sp => new { sp.SpeciesId, sp.FlickrPhotoId });

            entity.HasOne(sp => sp.Species)
                .WithMany()
                .HasForeignKey(sp => sp.SpeciesId);

            entity.HasOne(sp => sp.FlickrPhoto)
                .WithMany(p => p.SpeciesPhotos)
                .HasForeignKey(sp => sp.FlickrPhotoId);
        });

        // ── SpeciesColor ───────────────────────────────────────────────────

        modelBuilder.Entity<SpeciesColor>(entity =>
        {
            entity.HasIndex(sc => new { sc.SpeciesId, sc.Rank }).IsUnique();

            entity.HasOne(sc => sc.Species)
                .WithMany()
                .HasForeignKey(sc => sc.SpeciesId);
        });

        // ── iNaturalist entities ───────────────────────────────────────────

        modelBuilder.Entity<INatPhoto>(entity =>
        {
            entity.HasIndex(p => p.PhotoId).IsUnique();
        });

        modelBuilder.Entity<SpeciesINatPhoto>(entity =>
        {
            entity.HasKey(sp => new { sp.SpeciesId, sp.INatPhotoId });

            entity.HasOne(sp => sp.Species)
                .WithMany()
                .HasForeignKey(sp => sp.SpeciesId);

            entity.HasOne(sp => sp.INatPhoto)
                .WithMany(p => p.SpeciesPhotos)
                .HasForeignKey(sp => sp.INatPhotoId);
        });
    }
}