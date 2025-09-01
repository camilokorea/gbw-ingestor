using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Application.Services;
using eBird.Ingestor.Infrastructure.Data;
using eBird.Ingestor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eBird.Ingestor.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Host creado. El programa se iniciará.");

        using (var scope = host.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var ingestionService = services.GetRequiredService<IIngestionService>();
                
                var regionsToProcess = new List<string> { "CO", "US", "ES" }; 
                
                await ingestionService.ProcessRegionsAsync(regionsToProcess);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ha ocurrido un error fatal en la ejecución: {ex.Message}");
            }
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
                services.AddHttpClient();
                services.AddScoped<IEbirdApiClient, EbirdApiClient>();
                services.AddScoped<IIngestionService, IngestionService>();
            });
}
