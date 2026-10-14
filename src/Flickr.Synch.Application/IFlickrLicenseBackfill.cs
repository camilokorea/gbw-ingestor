using eBird.Ingestor.Domain;

namespace Flickr.Synch.Application;

/// <summary>
/// Fills <see cref="FlickrPhoto.LicenseId"/> for photos synced before licenses were recorded, then reports how many
/// species have a photo Birdetector may republish.
/// </summary>
public interface IFlickrLicenseBackfill
{
    /// <param name="photoInfoLimit">
    /// Cap on flickr.photos.getInfo calls (one per photo) for species-linked photos the group pools did not cover.
    /// Flickr allows ~3600 calls/hour, so large values take hours.
    /// </param>
    Task<LicenseCoverageReport> RunAsync(int photoInfoLimit, CancellationToken cancellationToken = default);
}

/// <summary>Licenses of the photos linked to species, and how many species each policy would leave with a photo.</summary>
/// <param name="PoolUpdates">Licenses filled from the group pools.</param>
/// <param name="PhotoInfoUpdates">Licenses filled one by one with flickr.photos.getInfo.</param>
/// <param name="LinkedPhotosByReuse">Species-linked Flickr photos per license class (known licenses only).</param>
/// <param name="LinkedPhotosUnknown">Species-linked Flickr photos whose license is still unknown.</param>
/// <param name="SpeciesWithAnyPhoto">Species with at least one Flickr or iNaturalist photo.</param>
/// <param name="SpeciesWithOpenPhoto">Species with a photo under an open license (CC BY, CC BY-SA, CC0, public domain).</param>
/// <param name="SpeciesWithOpenOrNonCommercialPhoto">Same, also counting non-commercial licenses (CC BY-NC, CC BY-NC-SA).</param>
public sealed record LicenseCoverageReport(
    int PoolUpdates,
    int PhotoInfoUpdates,
    IReadOnlyDictionary<LicenseReuse, int> LinkedPhotosByReuse,
    int LinkedPhotosUnknown,
    int SpeciesWithAnyPhoto,
    int SpeciesWithOpenPhoto,
    int SpeciesWithOpenOrNonCommercialPhoto);
