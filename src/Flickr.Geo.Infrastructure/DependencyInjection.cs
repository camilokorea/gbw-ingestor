using Flickr.Geo.Application;
using Flickr.Geo.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flickr.Geo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlickrGeoInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient("Flickr");
        services.AddScoped<IFlickrGeoService, FlickrGeoService>();
        return services;
    }
}
