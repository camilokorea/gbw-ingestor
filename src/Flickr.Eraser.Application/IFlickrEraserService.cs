namespace Flickr.Eraser.Application;

/// <summary>
/// Validates all Flickr photo URLs. Hard-deletes from DB any photo
/// whose Small320Url returns a non-200 HTTP status.
/// </summary>
public interface IFlickrEraserService
{
    Task EraseDeletedPhotosAsync(CancellationToken cancellationToken = default);
}
