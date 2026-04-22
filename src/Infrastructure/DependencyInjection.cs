using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Persistence;
using eBird.Ingestor.Infrastructure.Data;
using eBird.Ingestor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eBird.Ingestor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EbirdIngestorDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlServerOptionsAction: sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                }));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<EbirdIngestorDbContext>());
        services.AddScoped<eBird.Ingestor.Application.Contracts.IINatPhotoService, Services.INatPhotoService>();

        // Telemetry & alerting — registered once here, available in all workers
        services.AddHttpClient();
        services.AddSingleton<TelegramAlertService>();
        services.AddScoped<IWorkerTelemetry, WorkerTelemetryService>();

        return services;
    }
}
