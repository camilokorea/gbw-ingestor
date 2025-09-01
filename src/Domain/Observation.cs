namespace eBird.Ingestor.Domain;

/// <summary>
/// Representa una única observación de aves.
/// Esta es la entidad central del dominio.
/// </summary>
public class Observation
{
    /// <summary>
    /// Identificador único de la observación en la base de datos.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// La fecha y hora en que se realizó la observación.
    /// Clave para construir líneas de tiempo.
    /// </summary>
    public DateTime ObservationDate { get; set; }

    /// <summary>
    /// Cantidad de individuos observados.
    /// </summary>
    public int HowMany { get; set; }

    /// <summary>
    /// Clave foránea para la entidad Location.
    /// </summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>
    /// Propiedad de navegación a la ubicación de la observación.
    /// </summary>
    public Location Location { get; set; } = null!;

    /// <summary>
    /// Clave foránea para la entidad Species.
    /// </summary>
    public int SpeciesId { get; set; }

    /// <summary>
    /// Propiedad de navegación a la especie observada.
    /// </summary>
    public Species Species { get; set; } = null!;
}
