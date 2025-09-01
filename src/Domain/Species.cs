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
    /// Nombre común de la especie.
    /// </summary>
    public string CommonName { get; set; } = string.Empty;

    /// <summary>
    /// Nombre científico de la especie.
    /// </summary>
    public string ScientificName { get; set; } = string.Empty;

    /// <summary>
    /// Colección de observaciones para esta especie.
    /// </summary>
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}
