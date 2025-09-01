namespace eBird.Ingestor.Domain;

public class Location
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string CountryCode { get; set; } = string.Empty;
    public Country Country { get; set; } = null!;

    public int? StateId { get; set; }
    public State? State { get; set; }

    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}