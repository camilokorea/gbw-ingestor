using Sound.Ingestor.Application.Contracts.Dtos;

namespace Sound.Ingestor.Application.Contracts.Api;

public interface IXenoCantoApiClient
{
    Task<XenoCantoResponseDto> GetRecordingsAsync(string scientificName);
}
