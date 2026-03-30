namespace Flickr.Geo.Application;

/// <summary>
/// Fetches geolocation data from Flickr for any photo not yet geolocated
/// and persists it to FlickrPhotoLocations.
/// </summary>
public interface IFlickrGeoService
{
    Task GeolocatePhotosAsync(CancellationToken cancellationToken = default);
}
