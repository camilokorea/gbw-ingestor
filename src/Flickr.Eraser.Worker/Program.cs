using eBird.Ingestor.Infrastructure;
using Flickr.Eraser.Application;
using Flickr.Eraser.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flickr.Eraser.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Flickr Eraser Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<eBird.Ingestor.Application.Contracts.IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("flickr-eraser");

        try
        {
            var eraserService = services.GetRequiredService<IFlickrEraserService>();
            await eraserService.EraseDeletedPhotosAsync();
            await telemetry.CompleteAsync(runId);
            Console.WriteLine("Flickr Eraser Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Flickr Eraser Worker: {ex.Message}");
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
                services.AddFlickrEraserInfrastructure();
            });
}
