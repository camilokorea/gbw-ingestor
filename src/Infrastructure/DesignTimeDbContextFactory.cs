using eBird.Ingestor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace eBird.Ingestor.Infrastructure;

/// <summary>
/// Used only by EF Core design-time tools (dotnet ef migrations add/update).
/// Not used at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EbirdIngestorDbContext>
{
    public EbirdIngestorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EbirdIngestorDbContext>();
        // Design-time only — actual connection string is injected at runtime via appsettings.json
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=eBirdIngestor;User Id=sa;Password=P@ssw0rd;TrustServerCertificate=True");
        return new EbirdIngestorDbContext(optionsBuilder.Options);
    }
}
