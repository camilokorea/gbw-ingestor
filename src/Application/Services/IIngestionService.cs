namespace eBird.Ingestor.Application.Services;

public interface IIngestionService
{
    Task ProcessRegionsAsync(IEnumerable<string> regionCodes);
}