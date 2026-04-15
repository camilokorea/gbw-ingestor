namespace eBird.Ingestor.Application.Services;

public interface IINatIngestionService
{
    Task ProcessQueueAsync();
}
