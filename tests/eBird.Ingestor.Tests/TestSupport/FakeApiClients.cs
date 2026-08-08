using eBird.Ingestor.Application.Contracts;
using eBird.Ingestor.Application.Contracts.Dtos;
using eBird.Ingestor.Domain;
using Sound.Ingestor.Application.Contracts.Api;
using Sound.Ingestor.Application.Contracts.Dtos;

namespace eBird.Ingestor.Tests.TestSupport;

/// <summary>
/// Scriptable eBird client. Every method can be told to throw, which is how the tests drive the
/// "region failed halfway through" branch of the ingestion loop.
/// </summary>
public sealed class FakeEbirdApiClient : IEbirdApiClient
{
    public Dictionary<string, List<EbirdObservationDto>> ObservationsByRegion { get; } = new();
    public Dictionary<string, EbirdLocationDetailsDto> LocationDetailsByLocId { get; } = new();
    public Dictionary<(string SpeciesCode, string Locale), string?> LocalizedNames { get; } = new();

    public Exception? ObservationsFailure { get; set; }
    public List<string> RegionsRequested { get; } = new();
    public List<string> LocationsRequested { get; } = new();

    public Task<IEnumerable<EbirdObservationDto>> GetRecentObservationsAsync(string regionCode)
    {
        RegionsRequested.Add(regionCode);

        if (ObservationsFailure is not null)
        {
            throw ObservationsFailure;
        }

        var observations = ObservationsByRegion.TryGetValue(regionCode, out var list)
            ? list
            : new List<EbirdObservationDto>();

        return Task.FromResult<IEnumerable<EbirdObservationDto>>(observations);
    }

    public Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId)
    {
        LocationsRequested.Add(locId);

        return Task.FromResult(LocationDetailsByLocId.TryGetValue(locId, out var details)
            ? details
            : new EbirdLocationDetailsDto());
    }

    public Task<string?> GetSpeciesLocalizedNameAsync(string speciesCode, string locale = "es") =>
        Task.FromResult(LocalizedNames.TryGetValue((speciesCode, locale), out var name) ? name : null);
}

/// <summary>Scriptable iNaturalist client, keyed by place id and page.</summary>
public sealed class FakeINatApiClient : IINatApiClient
{
    public Dictionary<(int PlaceId, int Page), List<INatObservationDto>> ObservationsByPage { get; } = new();
    public Dictionary<(int TaxonId, string Locale), string?> LocalizedNames { get; } = new();

    public Exception? ObservationsFailure { get; set; }
    public List<(int PlaceId, int Page)> PagesRequested { get; } = new();

    public Task<IEnumerable<INatObservationDto>> GetRecentObservationsAsync(
        int inatPlaceId, int daysAgo, int page = 1)
    {
        PagesRequested.Add((inatPlaceId, page));

        if (ObservationsFailure is not null)
        {
            throw ObservationsFailure;
        }

        var observations = ObservationsByPage.TryGetValue((inatPlaceId, page), out var list)
            ? list
            : new List<INatObservationDto>();

        return Task.FromResult<IEnumerable<INatObservationDto>>(observations);
    }

    public Task<string?> GetTaxonLocalizedNameAsync(int taxonId, string locale) =>
        Task.FromResult(LocalizedNames.TryGetValue((taxonId, locale), out var name) ? name : null);
}

/// <summary>Scriptable iNaturalist photo service.</summary>
public sealed class FakeINatPhotoService : IINatPhotoService
{
    public Dictionary<string, long?> TaxonIdsByScientificName { get; } = new();
    public Dictionary<long, List<INatPhoto>> PhotosByTaxonId { get; } = new();

    public List<string> TaxonLookups { get; } = new();
    public List<long> PhotoLookups { get; } = new();

    public Task<long?> SearchTaxonIdByNameAsync(string scientificName, CancellationToken cancellationToken = default)
    {
        TaxonLookups.Add(scientificName);
        return Task.FromResult(TaxonIdsByScientificName.TryGetValue(scientificName, out var id) ? id : null);
    }

    public Task<List<INatPhoto>> GetPhotosForTaxonAsync(long taxonId, CancellationToken cancellationToken = default)
    {
        PhotoLookups.Add(taxonId);

        // Fresh entity instances per call: the real service deserializes new objects each time,
        // and reusing tracked instances would hide duplicate-insert bugs.
        var photos = PhotosByTaxonId.TryGetValue(taxonId, out var list) ? list : new List<INatPhoto>();
        return Task.FromResult(photos.Select(Clone).ToList());
    }

    private static INatPhoto Clone(INatPhoto photo) => new()
    {
        PhotoId = photo.PhotoId,
        Attribution = photo.Attribution,
        LicenseCode = photo.LicenseCode,
        INatObservationId = photo.INatObservationId,
        SquareUrl = photo.SquareUrl,
        SmallUrl = photo.SmallUrl,
        MediumUrl = photo.MediumUrl,
        LargeUrl = photo.LargeUrl,
        OriginalUrl = photo.OriginalUrl,
        WebUrl = photo.WebUrl
    };
}

/// <summary>Scriptable Xeno-Canto client, keyed by scientific name.</summary>
public sealed class FakeXenoCantoApiClient : IXenoCantoApiClient
{
    public Dictionary<string, XenoCantoResponseDto> RecordingsByScientificName { get; } = new();
    public List<string> NamesRequested { get; } = new();

    public Task<XenoCantoResponseDto> GetRecordingsAsync(string scientificName)
    {
        NamesRequested.Add(scientificName);

        return Task.FromResult(RecordingsByScientificName.TryGetValue(scientificName, out var response)
            ? response
            : new XenoCantoResponseDto());
    }
}
