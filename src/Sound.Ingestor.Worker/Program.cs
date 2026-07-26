using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using eBird.Ingestor.Infrastructure; // For AddInfrastructure (DbContext)
using Sound.Ingestor.Infrastructure; // For AddSoundInfrastructure (ApiClient)
using Sound.Ingestor.Application.Services;

namespace Sound.Ingestor.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Host del ingestor de sonidos creado. El programa se iniciará.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<eBird.Ingestor.Application.Contracts.IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("sound-ingestor");

        try
        {
            var ingestionService = services.GetRequiredService<ISoundIngestionService>();
            var recordsProcessed = await ingestionService.ProcessSoundsAsync();
            await telemetry.CompleteAsync(runId, recordsProcessed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Sound Ingestor Worker: {ex.Message}");
            await telemetry.FailAsync(runId, ex);
            Environment.Exit(1);
        }

        Console.WriteLine("Sound Ingestor Worker completed.");
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                // The sound ingestor will share the same appsettings.json
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // Add original infrastructure to get DbContext and connection strings
                services.AddInfrastructure(context.Configuration);

                // Add the new sound infrastructure for the Xeno-Canto client
                services.AddHttpClient();
                services.AddSoundInfrastructure();

                // Add the new application service
                services.AddScoped<ISoundIngestionService, SoundIngestionService>();
            });
}