using Microsoft.Extensions.DependencyInjection;
using Sound.Ingestor.Application.Contracts.Api;
using Sound.Ingestor.Infrastructure.Services;

namespace Sound.Ingestor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSoundInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IXenoCantoApiClient, XenoCantoApiClient>();
        return services;
    }
}
