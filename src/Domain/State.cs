namespace eBird.Ingestor.Domain;

/// <summary>
/// Representa un estado, provincia o subdivisión de primer nivel de un país.
/// </summary>
public class State
{
    public int Id { get; set; }

    /// <summary>
    /// Código del estado (ej. "US-NY").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del estado.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Código del país al que pertenece (Clave Foránea).
    /// </summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// Propiedad de navegación al país.
    /// </summary>
    public Country Country { get; set; } = null!;

    /// <summary>
    /// Colección de ubicaciones que pertenecen a este estado.
    /// </summary>
    public ICollection<Location> Locations { get; set; } = new List<Location>();
}
