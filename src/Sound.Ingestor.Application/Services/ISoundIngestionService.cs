namespace Sound.Ingestor.Application.Services;

public interface ISoundIngestionService
{
    /// <summary>Ingests Xeno-Canto recordings and returns the number of new records saved.</summary>
    Task<int> ProcessSoundsAsync();
}
