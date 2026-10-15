using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Infrastructure;
using eBird.Ingestor.Infrastructure.Data;
using eBird.Ingestor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Commons.Photo.Worker;

/// <summary>
/// Weekly: finds an open-licensed Wikimedia Commons photo for every species (via Wikidata) and stores it with its
/// author and license, so social posts can republish it with proper credit.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Commons Photo Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("commons-photo");

        try
        {
            var dbContext = services.GetRequiredService<EbirdIngestorDbContext>();
            await dbContext.Database.MigrateAsync();

            var report = await services.GetRequiredService<ICommonsPhotoIngestionService>().SyncAsync();

            await telemetry.CompleteAsync(runId, report.Inserted + report.Updated + report.Deleted);
            Console.WriteLine("Commons Photo Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Commons Photo Worker: {ex.Message}");
            await telemetry.FailAsync(runId, ex);
            Environment.Exit(1);
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);

                // Wikimedia's bot policy asks for a User-Agent that identifies the tool and how to reach it.
                services.AddHttpClient(CommonsApiClient.HttpClientName, client =>
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Birdetector-Bot/1.0 (https://birdetector.com)");
                    client.Timeout = TimeSpan.FromSeconds(90); // SPARQL batches can take a while
                });

                services.AddScoped<ICommonsApiClient, CommonsApiClient>();
                services.AddScoped<ICommonsPhotoIngestionService, CommonsPhotoIngestionService>();
            });
}
