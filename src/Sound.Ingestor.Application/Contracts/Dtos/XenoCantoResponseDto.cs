using System.Text.Json.Serialization;

namespace Sound.Ingestor.Application.Contracts.Dtos;

public class XenoCantoResponseDto
{
    [JsonPropertyName("recordings")]
    public List<XenoCantoRecordingDto> Recordings { get; set; } = new();
}
