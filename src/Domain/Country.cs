namespace eBird.Ingestor.Domain;

/// <summary>
/// Representa un país.
/// </summary>
public class Country
{
    /// <summary>
    /// Código del país (ej. "CO", "US"). Es la clave primaria.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del país.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Colección de estados que pertenecen a este país.
    /// </summary>
    public ICollection<State> States { get; set; } = new List<State>();

    /// <summary>
    /// Colección de ubicaciones que pertenecen a este país.
    /// </summary>
    public ICollection<Location> Locations { get; set; } = new List<Location>();
}
