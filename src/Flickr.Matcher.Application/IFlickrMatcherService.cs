namespace Flickr.Matcher.Application;

/// <summary>
/// Matches unlinked FlickrPhotos to Species by finding the species'
/// scientific name as a substring of the photo title.
/// </summary>
public interface IFlickrMatcherService
{
    Task MatchPhotosToSpeciesAsync(CancellationToken cancellationToken = default);
}
