namespace eBird.Ingestor.Application.Services;

public interface IQueueSeederService
{
    Task SeedQueueAsync();
}
