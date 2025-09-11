using eBird.Ingestor.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sound.Ingestor.Application.Contracts.Api;
using Sound.Ingestor.Domain;

namespace Sound.Ingestor.Application.Services;

public class SoundIngestionService : ISoundIngestionService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IXenoCantoApiClient _xenoCantoApiClient;
    private readonly ILogger<SoundIngestionService> _logger;

    public SoundIngestionService(
        IApplicationDbContext dbContext,
        IXenoCantoApiClient xenoCantoApiClient,
        ILogger<SoundIngestionService> logger)
    {
        _dbContext = dbContext;
        _xenoCantoApiClient = xenoCantoApiClient;
        _logger = logger;
    }

    public async Task ProcessSoundsAsync()
    {
        _logger.LogInformation("Iniciando el proceso de ingestión de sonidos de Xeno-Canto.");

        var speciesList = await _dbContext.Species.AsNoTracking().ToListAsync();
        if (!speciesList.Any())
        {
            _logger.LogWarning("No se encontraron especies en la base de datos. El proceso de ingestión de sonidos no puede continuar.");
            return;
        }

        foreach (var species in speciesList)
        {
            _logger.LogInformation("Buscando grabaciones para la especie: {ScientificName}", species.ScientificName);

            var response = await _xenoCantoApiClient.GetRecordingsAsync(species.ScientificName);

            if (response == null || !response.Recordings.Any())
            {
                _logger.LogInformation("No se encontraron grabaciones para {ScientificName}.", species.ScientificName);
                continue;
            }

            foreach (var recordingDto in response.Recordings)
            {
                var xenoCantoId = int.Parse(recordingDto.Id);
                var signatureExists = await _dbContext.SoundSignatures.AnyAsync(s => s.XenoCantoId == xenoCantoId);

                if (!signatureExists)
                {
                    var newSignature = new SoundSignature
                    {
                        XenoCantoId = xenoCantoId,
                        SpeciesId = species.Id,
                        SoundType = recordingDto.Type,
                        AudioUrl = recordingDto.AudioUrl, // The API returns a protocol-relative URL
                        RecordistUrl = recordingDto.RecordistUrl,
                        License = "https:" + recordingDto.License, // The API returns a protocol-relative URL
                        Quality = recordingDto.Quality
                    };
                    await _dbContext.SoundSignatures.AddAsync(newSignature);
                }
            }
        }

        var changes = await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Proceso de ingestión de sonidos finalizado. Se guardaron {Count} nuevas grabaciones.", changes);
    }
}
