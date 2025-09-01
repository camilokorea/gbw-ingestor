using System.Text.Json.Serialization;

namespace eBird.Ingestor.Application.Contracts.Dtos;

public class EbirdObservationDto
{
    [JsonPropertyName("speciesCode")]
    public string SpeciesCode { get; set; } = string.Empty;

    [JsonPropertyName("comName")]
    public string ComName { get; set; } = string.Empty;

    [JsonPropertyName("sciName")]
    public string SciName { get; set; } = string.Empty;

    [JsonPropertyName("locId")]
    public string LocId { get; set; } = string.Empty;

    [JsonPropertyName("locName")]
    public string LocName { get; set; } = string.Empty;

    [JsonPropertyName("obsDt")]
    public string ObsDt { get; set; } = string.Empty;

    [JsonPropertyName("howMany")]
    public int HowMany { get; set; }

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Lng { get; set; }
}