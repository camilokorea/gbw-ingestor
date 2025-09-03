namespace eBird.Ingestor.Application.Services;

public interface IIngestionService
{
    Task ProcessQueueAsync();
}