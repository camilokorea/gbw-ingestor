using eBird.Ingestor.Infrastructure;
using Flickr.Synch.Application;
using Flickr.Synch.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flickr.Synch.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("Flickr Synch Worker starting.");

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var synchService = services.GetRequiredService<IFlickrSynchService>();
            await synchService.SynchronizeAsync();
            Console.WriteLine("Flickr Synch Worker completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in Flickr Synch Worker: {ex.Message}");
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
                services.AddFlickrSynchInfrastructure();
            });
}
