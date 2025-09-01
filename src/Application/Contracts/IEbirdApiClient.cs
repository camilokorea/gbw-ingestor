using eBird.Ingestor.Application.Contracts.Dtos;

namespace eBird.Ingestor.Application.Contracts;

public interface IEbirdApiClient
{
    Task<IEnumerable<EbirdObservationDto>> GetRecentObservationsAsync(string regionCode);
    Task<EbirdLocationDetailsDto> GetLocationDetailsAsync(string locId);
}