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

    // Persist recordings in fixed-size batches instead of one save at the end of the ~4h run.
    // This keeps change-tracker memory flat and makes partial progress durable/resumable.
    private const int BatchSize = 500;

    public async Task<int> ProcessSoundsAsync()
    {
        _logger.LogInformation("Iniciando el proceso de ingestión de sonidos de Xeno-Canto.");

        var speciesList = await _dbContext.Species.AsNoTracking().ToListAsync();
        if (!speciesList.Any())
        {
            _logger.LogWarning("No se encontraron especies en la base de datos. El proceso de ingestión de sonidos no puede continuar.");
            return 0;
        }

        // Ids already queued/seen in THIS run — guards the unique index on XenoCantoId, since a
        // recording can be returned for more than one species and pending inserts aren't yet
        // visible to the AnyAsync existence check below.
        var seenIds = new HashSet<int>();
        var pending = 0;      // new signatures added since the last save
        var totalSaved = 0;

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
                if (!int.TryParse(recordingDto.Id, out var xenoCantoId))
                {
                    _logger.LogWarning("Se omitió una grabación con Id no numérico de Xeno-Canto: '{Id}'.", recordingDto.Id);
                    continue;
                }

                if (!seenIds.Add(xenoCantoId))
                {
                    continue; // already queued earlier in this run
                }

                var signatureExists = await _dbContext.SoundSignatures.AnyAsync(s => s.XenoCantoId == xenoCantoId);
                if (signatureExists)
                {
                    continue;
                }

                _dbContext.SoundSignatures.Add(new SoundSignature
                {
                    XenoCantoId = xenoCantoId,
                    SpeciesId = species.Id,
                    SoundType = recordingDto.Type,
                    AudioUrl = NormalizeUrl(recordingDto.AudioUrl),
                    RecordistUrl = NormalizeUrl(recordingDto.RecordistUrl),
                    License = NormalizeUrl(recordingDto.License),
                    Quality = recordingDto.Quality
                });
                pending++;

                if (pending >= BatchSize)
                {
                    totalSaved += await SaveBatchAsync();
                    pending = 0;
                }
            }
        }

        // Flush the final partial batch.
        totalSaved += await SaveBatchAsync();

        _logger.LogInformation("Proceso de ingestión de sonidos finalizado. Se guardaron {Count} nuevas grabaciones.", totalSaved);
        return totalSaved;
    }

    /// <summary>
    /// Saves the currently-tracked batch of new signatures and clears the change tracker so the
    /// next 500 start from a clean, low-memory state.
    /// </summary>
    private async Task<int> SaveBatchAsync()
    {
        var saved = await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
        if (saved > 0)
        {
            _logger.LogInformation("Se guardó un lote de {Count} nuevas grabaciones.", saved);
        }
        return saved;
    }

    /// <summary>
    /// Normalizes URLs returned by the Xeno-Canto API. Historically the API returned
    /// protocol-relative URLs (<c>//xeno-canto.org/…</c>); v3 may return either those or
    /// absolute URLs. This prepends <c>https:</c> only for protocol-relative values so we
    /// never produce malformed strings like <c>https:https://…</c>.
    /// </summary>
    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        url = url.Trim();
        return url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
    }
}
