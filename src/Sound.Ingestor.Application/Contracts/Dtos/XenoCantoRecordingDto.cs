using System.Text.Json.Serialization;

namespace Sound.Ingestor.Application.Contracts.Dtos;

public class XenoCantoRecordingDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("q")]
    public string Quality { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string AudioUrl { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string RecordistUrl { get; set; } = string.Empty;

    [JsonPropertyName("lic")]
    public string License { get; set; } = string.Empty;
}
