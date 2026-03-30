using Flickr.Matcher.Application;
using Flickr.Matcher.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flickr.Matcher.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFlickrMatcherInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IFlickrMatcherService, FlickrMatcherService>();
        return services;
    }
}
