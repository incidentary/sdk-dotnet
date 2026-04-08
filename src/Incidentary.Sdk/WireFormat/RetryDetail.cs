using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>Retry observation detail for a causal event.</summary>
public sealed class RetryDetail
{
    [JsonPropertyName("explicit_observed")]
    public bool? ExplicitObserved { get; init; }

    [JsonPropertyName("key_quality")]
    public string? KeyQuality { get; init; }

    [JsonPropertyName("edge_key")]
    public string? EdgeKey { get; init; }

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; init; }
}
