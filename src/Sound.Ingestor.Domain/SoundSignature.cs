
using eBird.Ingestor.Domain;

namespace Sound.Ingestor.Domain;

/// <summary>
/// Represents a single bird sound signature/recording.
/// </summary>
public class SoundSignature
{
    /// <summary>
    /// Unique identifier for the sound signature in the database.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The recording ID from the Xeno-Canto API.
    /// </summary>
    public int XenoCantoId { get; set; }

    /// <summary>
    /// The type of sound (e.g., "song", "call").
    /// </summary>
    public string SoundType { get; set; } = string.Empty;

    /// <summary>
    /// The URL to the audio file.
    /// </summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>
    /// The URL to the recording page on Xeno-Canto.
    /// </summary>
    public string RecordistUrl { get; set; } = string.Empty;

    /// <summary>
    /// The license of the recording.
    /// </summary>
    public string License { get; set; } = string.Empty;

    /// <summary>
    /// The quality rating of the recording.
    /// </summary>
    public string Quality { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the Species entity.
    /// </summary>
    public int SpeciesId { get; set; }

    /// <summary>
    /// Navigation property to the Species.
    /// </summary>
    public Species Species { get; set; } = null!;
}
