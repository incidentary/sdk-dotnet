using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>SDK self-telemetry included in each batch.</summary>
public sealed class SdkTelemetry
{
    [JsonPropertyName("sdk_version")]
    public required string SdkVersion { get; init; }

    [JsonPropertyName("sdk_language")]
    public string SdkLanguage { get; init; } = "dotnet";

    [JsonPropertyName("queue_depth")]
    public long QueueDepth { get; init; }

    [JsonPropertyName("dropped_ce_count")]
    public long DroppedCeCount { get; init; }

    [JsonPropertyName("flush_latency_ms")]
    public long FlushLatencyMs { get; init; }
}
