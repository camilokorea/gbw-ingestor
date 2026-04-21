using System.Text.Json.Serialization;

namespace eBird.Ingestor.Application.Contracts.Dtos;

/// <summary>
/// Maps the iNaturalist /observations API response object.
/// </summary>
public class INatObservationDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("observed_on")]
    public string? ObservedOn { get; set; }

    [JsonPropertyName("num_identification_agreements")]
    public int NumIdentificationAgreements { get; set; }

    [JsonPropertyName("place_guess")]
    public string? PlaceGuess { get; set; }

    [JsonPropertyName("taxon")]
    public INatTaxonDto Taxon { get; set; } = null!;

    [JsonPropertyName("identifications")]
    public List<INatIdentificationDto>? Identifications { get; set; }

    [JsonPropertyName("geojson")]
    public INatGeoJsonDto? Geojson { get; set; }
}

public class INatIdentificationDto
{
    [JsonPropertyName("taxon")]
    public INatTaxonDetailDto Taxon { get; set; } = null!;
}

public class INatTaxonDetailDto
{
    [JsonPropertyName("ancestors")]
    public List<INatAncestorDto>? Ancestors { get; set; }
}

public class INatAncestorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("rank")]
    public string Rank { get; set; } = string.Empty;
}

public class INatTaxonDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Scientific name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Locale-aware common name (locale param passed in request).</summary>
    [JsonPropertyName("preferred_common_name")]
    public string? PreferredCommonName { get; set; }

    /// <summary>Must equal "Aves" — used as a guard filter.</summary>
    [JsonPropertyName("iconic_taxon_name")]
    public string? IconicTaxonName { get; set; }

    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("order")]
    public string? Order { get; set; }
}

public class INatGeoJsonDto
{
    /// <summary>GeoJSON Point coordinates: [longitude, latitude].</summary>
    [JsonPropertyName("coordinates")]
    public double[]? Coordinates { get; set; }
}
