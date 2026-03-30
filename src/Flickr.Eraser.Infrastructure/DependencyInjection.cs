using Flickr.Eraser.Application;
using Flickr.Eraser.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flickr.Eraser.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlickrEraserInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient("Flickr");
        services.AddScoped<IFlickrEraserService, FlickrEraserService>();
        return services;
    }
}
