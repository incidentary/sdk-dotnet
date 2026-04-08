using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>Downstream dependency detail for a causal event.</summary>
public sealed class DownstreamDetail
{
    [JsonPropertyName("edge_key")]
    public string? EdgeKey { get; init; }

    [JsonPropertyName("service")]
    public string? Service { get; init; }

    [JsonPropertyName("operation_name")]
    public string? OperationName { get; init; }

    [JsonPropertyName("key_quality")]
    public string? KeyQuality { get; init; }
}
