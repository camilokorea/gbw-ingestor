using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Domain;
using Microsoft.EntityFrameworkCore;

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
        });
    }
}