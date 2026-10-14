using Microsoft.EntityFrameworkCore;
using eBird.Ingestor.Infrastructure;
using Flickr.Synch.Application;
using Flickr.Synch.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flickr.Synch.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Flickr Synch Worker starting.");

        // One-off mode: --backfill-licenses [--photo-info-limit=N] fills the license of photos synced before
        // licenses were recorded, and prints how many species have a republishable photo.
        var backfill = args.Contains("--backfill-licenses");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<eBird.Ingestor.Application.Contracts.IWorkerTelemetry>();
        var runId = await telemetry.StartAsync(backfill ? "flickr-license-backfill" : "flickr-synch");

        try
        {
            // Automatic migration on startup
            var dbContext = services.GetRequiredService<eBird.Ingestor.Infrastructure.Data.EbirdIngestorDbContext>();
            await dbContext.Database.MigrateAsync();

            if (backfill)
            {
                var limit = args.FirstOrDefault(a => a.StartsWith("--photo-info-limit="))?.Split('=')[1];
                var report = await services.GetRequiredService<IFlickrLicenseBackfill>()
                    .RunAsync(limit is null ? 3000 : int.Parse(limit));
                await telemetry.CompleteAsync(runId, report.PoolUpdates + report.PhotoInfoUpdates);
                return;
            }

            var synchService = services.GetRequiredService<IFlickrSynchService>();
            await synchService.SynchronizeAsync();
            await telemetry.CompleteAsync(runId);
            Console.WriteLine("Flickr Synch Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Flickr Synch Worker: {ex.Message}");
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
                services.AddFlickrSynchInfrastructure();
            });
}
