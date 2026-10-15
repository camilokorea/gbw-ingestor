using eBird.Ingestor.Infrastructure;
using Flickr.Geo.Application;
using Flickr.Geo.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flickr.Geo.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Flickr Geo Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<eBird.Ingestor.Application.Contracts.IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("flickr-geo");

        try
        {
            var geoService = services.GetRequiredService<IFlickrGeoService>();
            var report = await geoService.GeolocatePhotosAsync();
            await telemetry.CompleteAsync(runId, report.Located + report.WithoutLocation);
            Console.WriteLine("Flickr Geo Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Flickr Geo Worker: {ex.Message}");
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
                services.AddFlickrGeoInfrastructure();
            });
}
