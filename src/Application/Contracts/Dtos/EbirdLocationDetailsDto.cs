using System.Text.Json.Serialization;

namespace eBird.Ingestor.Application.Contracts.Dtos;

public class EbirdLocationDetailsDto
{
    [JsonPropertyName("subnational1Code")]
    public string? StateCode { get; set; }

    [JsonPropertyName("subnational1Name")]
    public string? StateName { get; set; }
}