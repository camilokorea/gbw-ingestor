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
using Microsoft.EntityFrameworkCore;

namespace eBird.Ingestor.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        if (args.Contains("--seed-queue"))
        {
            var seeder = services.GetRequiredService<IQueueSeederService>();
            await seeder.SeedQueueAsync();
            return;
        }

        Console.WriteLine("Host creado. El programa se iniciará.");

        try
        {
            var ingestionService = services.GetRequiredService<IIngestionService>();
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
                services.AddDbContext<EbirdIngestorDbContext>(options =>
                    options.UseSqlServer(
                        context.Configuration.GetConnectionString("DefaultConnection"),
                        sqlServerOptionsAction: sqlOptions =>
                        {
                            sqlOptions.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorNumbersToAdd: null);
                        }));

                services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<EbirdIngestorDbContext>());

                services.AddHttpClient<IEbirdApiClient, EbirdApiClient>()
                    .AddPolicyHandler(GetRetryPolicy());

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
