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

        try
        {
            var ingestionService = services.GetRequiredService<ISoundIngestionService>();
            await ingestionService.ProcessSoundsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ha ocurrido un error fatal en la ejecución: {ex.Message}");
            // Consider using a logger here in a real application
        }

        Console.WriteLine("Proceso de ingestión de sonidos finalizado.");
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