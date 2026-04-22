using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Services;
using Microsoft.EntityFrameworkCore;
using eBird.Ingestor.Infrastructure;
using eBird.Ingestor.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;

namespace iNat.Ingestor.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("iNat Ingestor Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var telemetry = services.GetRequiredService<eBird.Ingestor.Application.Contracts.IWorkerTelemetry>();
        var runId = await telemetry.StartAsync("inat-ingestor");

        try
        {
            var dbContext = services.GetRequiredService<eBird.Ingestor.Infrastructure.Data.EbirdIngestorDbContext>();
            await dbContext.Database.MigrateAsync();
            var seeder = services.GetRequiredService<IINatQueueSeederService>();
            var ingestionService = services.GetRequiredService<IINatIngestionService>();

            if (args.Contains("--seed-queue"))
            {
                await seeder.SeedQueueAsync();
                await telemetry.CompleteAsync(runId);
                return;
            }

            var processedQueue = await seeder.CheckQueueCompletedAsync();
            if (!processedQueue)
                await seeder.SeedQueueAsync();

            await ingestionService.ProcessQueueAsync();
            await telemetry.CompleteAsync(runId);
            Console.WriteLine("iNat Ingestor Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in iNat Ingestor Worker: {ex.Message}");
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

                services.AddHttpClient();
                services.AddScoped<IINatApiClient, INatApiClient>();

                services.AddScoped<IINatQueueSeederService, INatQueueSeederService>();
                services.AddScoped<IINatIngestionService, INatIngestionService>();
            });

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => (int)msg.StatusCode == 429)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) * 5));
    }
}
