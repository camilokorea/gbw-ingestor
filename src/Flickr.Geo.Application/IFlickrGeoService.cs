namespace Flickr.Geo.Application;

/// <summary>What one geolocation run did.</summary>
/// <param name="Located">Photos that got a location.</param>
/// <param name="WithoutLocation">Photos Flickr has no location for (or no longer serves): checked, never asked again.</param>
/// <param name="Skipped">Photos left for a later run (transient errors).</param>
/// <param name="StoppedByThrottling">The run ended early because Flickr kept answering 429.</param>
public sealed record GeoRunReport(int Located, int WithoutLocation, int Skipped, bool StoppedByThrottling);

/// <summary>
/// Fetches geolocation data from Flickr for photos not yet checked and persists it to FlickrPhotoLocations.
/// </summary>
public interface IFlickrGeoService
{
    Task<GeoRunReport> GeolocatePhotosAsync(CancellationToken cancellationToken = default);
}
