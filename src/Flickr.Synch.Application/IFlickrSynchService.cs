using eBird.Ingestor.Domain;

namespace Flickr.Synch.Application;

/// <summary>
/// Service that paginates the configured Flickr group pools and
/// inserts any photo not yet present in the database.
/// </summary>
public interface IFlickrSynchService
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
