using eBird.Ingestor.Domain;
using eBird.Ingestor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: false);
    })
    .ConfigureServices((hostContext, services) =>
    {
        var connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<EbirdIngestorDbContext>(options =>
            options.UseSqlServer(connectionString));
    })
    .Build();

using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<EbirdIngestorDbContext>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TaxonomyBackfill");
var config = host.Services.GetRequiredService<IConfiguration>();

var apiKey = config["eBirdApi:ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    logger.LogError("API Key missing in appsettings.");
    return;
}

logger.LogInformation("Loading existing Species from database...");

var dbSpecies = await db.Species.ToListAsync();
var dbSpeciesDict = dbSpecies.ToDictionary(s => s.SpeciesCode, s => s);

logger.LogInformation("Found {Count} species in the database.", dbSpecies.Count);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

logger.LogInformation("Fetching global taxonomy from eBird...");

var url = "https://api.ebird.org/v2/ref/taxonomy/ebird?fmt=json";
var response = await httpClient.GetAsync(url);
response.EnsureSuccessStatusCode();

var json = await response.Content.ReadAsStringAsync();

using var doc = JsonDocument.Parse(json);
if (doc.RootElement.ValueKind != JsonValueKind.Array)
{
    logger.LogError("Unexpected JSON format from API.");
    return;
}

int updatedCount = 0;

foreach (var element in doc.RootElement.EnumerateArray())
{
    if (!element.TryGetProperty("speciesCode", out var codeProp) || codeProp.ValueKind != JsonValueKind.String)
        continue;

    var speciesCode = codeProp.GetString()!;

    if (dbSpeciesDict.TryGetValue(speciesCode, out var dbRecord))
    {
        bool changed = false;

        if (element.TryGetProperty("order", out var orderProp) && orderProp.ValueKind == JsonValueKind.String)
        {
            var order = orderProp.GetString();
            if (dbRecord.Order != order)
            {
                dbRecord.Order = order;
                changed = true;
            }
        }

        if (element.TryGetProperty("familyComName", out var famComProp) && famComProp.ValueKind == JsonValueKind.String)
        {
            var familyComName = famComProp.GetString();
            if (dbRecord.FamilyComName != familyComName)
            {
                dbRecord.FamilyComName = familyComName;
                changed = true;
            }
        }

        if (element.TryGetProperty("familySciName", out var famSciProp) && famSciProp.ValueKind == JsonValueKind.String)
        {
            var familySciName = famSciProp.GetString();
            if (dbRecord.FamilySciName != familySciName)
            {
                dbRecord.FamilySciName = familySciName;
                changed = true;
            }
        }

        if (changed) updatedCount++;
    }
}

if (updatedCount > 0)
{
    logger.LogInformation("Updating {Count} species with new taxonomy data in database...", updatedCount);
    await db.SaveChangesAsync();
    logger.LogInformation("Database taxonomy backfilled successfully!");
}
else
{
    logger.LogInformation("All species are already perfectly fully hydrated with taxonomy.");
}
