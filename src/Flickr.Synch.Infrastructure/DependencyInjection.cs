using Flickr.Synch.Application;
using Flickr.Synch.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flickr.Synch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlickrSynchInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient("Flickr");
        services.AddScoped<IFlickrSynchService, FlickrSynchService>();
        return services;
    }
}
