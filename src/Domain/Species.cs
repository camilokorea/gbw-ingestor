namespace eBird.Ingestor.Domain;

/// <summary>
/// Representa una especie de ave.
/// </summary>
public class Species
{
    /// <summary>
    /// Identificador único de la especie en la base de datos.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Código único de la especie según eBird.
    /// </summary>
    public string SpeciesCode { get; set; } = string.Empty;

    /// <summary>
    /// Collection of localized common names for this species.
    /// </summary>
    public ICollection<SpeciesTranslation> Translations { get; set; } = new List<SpeciesTranslation>();

    /// <summary>
    /// Nombre científico de la especie.
    /// </summary>
    public string ScientificName { get; set; } = string.Empty;

    /// <summary>
    /// Colección de observaciones para esta especie.
    /// </summary>
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}
