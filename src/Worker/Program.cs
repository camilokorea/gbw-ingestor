using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Infrastructure.Data;
using eBird.Ingestor.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;


using eBird.Ingestor.Infrastructure;

namespace eBird.Ingestor.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var seeder = services.GetRequiredService<IQueueSeederService>();

        if (args.Contains("--seed-queue"))
        {
            await seeder.SeedQueueAsync();
            return;
        }

        Console.WriteLine("Host creado. El programa se iniciará.");

        try
        {
            // --- Diagnostic Block Start ---
            var httpClientFactory = services.GetService<IHttpClientFactory>();
            var configuration = services.GetService<IConfiguration>();

            if (httpClientFactory == null)
            {
                Console.WriteLine("ERROR: IHttpClientFactory could not be resolved.");
            }
            else
            {
                Console.WriteLine("IHttpClientFactory resolved successfully.");
            }

            if (configuration == null)
            {
                Console.WriteLine("ERROR: IConfiguration could not be resolved.");
            }
            else
            {
                Console.WriteLine("IConfiguration resolved successfully.");
            }
            // --- Diagnostic Block End ---
            var processedQueue = await seeder.CheckQueueCompletedAsync();
            var ingestionService = services.GetRequiredService<IIngestionService>();
            if (!processedQueue)
                await seeder.SeedQueueAsync();
            await ingestionService.ProcessQueueAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ha ocurrido un error fatal en la ejecución: {ex.Message}");
        }
    }



    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);

                services.AddHttpClient(); // Keep this to register IHttpClientFactory
                services.AddScoped<IEbirdApiClient, EbirdApiClient>();

                services.AddScoped<IIngestionService, IngestionService>();
                services.AddScoped<IQueueSeederService, QueueSeederService>();
            });

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }


}
