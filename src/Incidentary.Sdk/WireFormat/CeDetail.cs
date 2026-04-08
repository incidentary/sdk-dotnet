using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>Optional enrichment for FULL capture mode.</summary>
public sealed class CeDetail
{
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("route_key")]
    public string? RouteKey { get; init; }

    [JsonPropertyName("route_template")]
    public string? RouteTemplate { get; init; }

    [JsonPropertyName("request_bytes")]
    public long? RequestBytes { get; init; }

    [JsonPropertyName("response_bytes")]
    public long? ResponseBytes { get; init; }

    [JsonPropertyName("request_headers")]
    public Dictionary<string, string>? RequestHeaders { get; init; }

    [JsonPropertyName("response_headers")]
    public Dictionary<string, string>? ResponseHeaders { get; init; }

    [JsonPropertyName("retry")]
    public RetryDetail? Retry { get; init; }

    [JsonPropertyName("downstream")]
    public DownstreamDetail? Downstream { get; init; }

    [JsonPropertyName("local_error_classification")]
    public string? LocalErrorClassification { get; init; }

    [JsonPropertyName("payload_snippet")]
    public string? PayloadSnippet { get; init; }
}
