using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Infrastructure;
using eBird.Ingestor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace INat.Photo.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("iNaturalist Photo Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("inat-photo");

        try
        {
            var dbContext = services.GetRequiredService<EbirdIngestorDbContext>();
            await dbContext.Database.MigrateAsync();

            var ingestionService = services.GetRequiredService<IINatPhotoIngestionService>();
            await ingestionService.ProcessSpeciesPhotosAsync();

            await telemetry.CompleteAsync(runId);
            Console.WriteLine("iNaturalist Photo Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in iNaturalist Photo Worker: {ex.Message}");
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

                services.AddHttpClient("INat", client =>
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Birdetector-Bot/1.0 (https://birdetector.com)");
                });

                services.AddScoped<IINatPhotoIngestionService, INatPhotoIngestionService>();
            });
}
