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
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TranslationBackfill");
var config = host.Services.GetRequiredService<IConfiguration>();

var apiKey = config["eBirdApi:ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    logger.LogError("API Key missing in appsettings.");
    return;
}

logger.LogInformation("Loading existing Species and Translations from database...");

// Make a lookup of what we already have
var dbSpecies = await db.Species.Include(s => s.Translations).ToListAsync();
var speciesDict = dbSpecies.ToDictionary(s => s.SpeciesCode, s => s);

logger.LogInformation("Found {Count} species in the database.", dbSpecies.Count);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-eBirdApiToken", apiKey);

var targetLocales = new[] { "es", "pt", "fr" };
var newTranslationsCount = 0;

foreach (var locale in targetLocales)
{
    logger.LogInformation("Fetching massive taxonomy for locale: {Locale}...", locale);
    
    var url = $"https://api.ebird.org/v2/ref/taxonomy/ebird?locale={locale}&fmt=json";
    var response = await httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    
    using var doc = JsonDocument.Parse(json);
    if (doc.RootElement.ValueKind != JsonValueKind.Array)
    {
        logger.LogError("Unexpected JSON format from API for locale {Locale}.", locale);
        continue;
    }

    // Build lookup for this language
    var apiLookup = new Dictionary<string, string>();
    foreach (var element in doc.RootElement.EnumerateArray())
    {
        if (element.TryGetProperty("speciesCode", out var codeProp) && codeProp.ValueKind == JsonValueKind.String &&
            element.TryGetProperty("comName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            apiLookup[codeProp.GetString()!] = nameProp.GetString()!;
        }
    }

    logger.LogInformation("Fetched {Count} translated species names from eBird for {Locale}.", apiLookup.Count, locale);

    // Now loop over DB species and add translations if missing
    foreach (var species in dbSpecies)
    {
        // Don't add if already exists
        if (species.Translations.Any(t => t.Locale == locale))
            continue;

        if (apiLookup.TryGetValue(species.SpeciesCode, out var localizedName))
        {
            var translation = new SpeciesTranslation
            {
                SpeciesId = species.Id,
                Locale = locale,
                CommonName = localizedName
            };
            
            db.SpeciesTranslations.Add(translation);
            newTranslationsCount++;
        }
    }
}

if (newTranslationsCount > 0)
{
    logger.LogInformation("Saving {Count} new translations to database...", newTranslationsCount);
    await db.SaveChangesAsync();
    logger.LogInformation("Database seeded successfully!");
}
else
{
    logger.LogInformation("No new translations needed.");
}
