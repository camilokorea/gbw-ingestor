namespace eBird.Ingestor.Application.Services;

public interface IINatQueueSeederService
{
    Task SeedQueueAsync();
    Task<bool> CheckQueueCompletedAsync();
}
