using Microsoft.EntityFrameworkCore;
using eBird.Ingestor.Infrastructure;
using Flickr.Matcher.Application;
using Flickr.Matcher.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flickr.Matcher.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Flickr Matcher Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            // Automatic migration on startup
            var dbContext = services.GetRequiredService<eBird.Ingestor.Infrastructure.Data.EbirdIngestorDbContext>();
            await dbContext.Database.MigrateAsync();

            var matcherService = services.GetRequiredService<IFlickrMatcherService>();
            await matcherService.MatchPhotosToSpeciesAsync();
            Console.WriteLine("Flickr Matcher Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Flickr Matcher Worker: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
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
                services.AddFlickrMatcherInfrastructure();
            });
}
