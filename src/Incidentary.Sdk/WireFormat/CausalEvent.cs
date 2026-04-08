using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>A causal event on the wire (SkeletonCe).</summary>
public sealed class CausalEvent
{
    [JsonPropertyName("ce_id")]
    public required string CeId { get; init; }

    [JsonPropertyName("trace_id")]
    public required string TraceId { get; init; }

    [JsonPropertyName("parent_ce_id")]
    public string? ParentCeId { get; init; }

    [JsonPropertyName("service_id")]
    public required string ServiceId { get; init; }

    [JsonPropertyName("wall_ts_ns")]
    public required long WallTsNs { get; init; }

    [JsonPropertyName("kind")]
    public required CeKind Kind { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    [JsonPropertyName("event_class")]
    public string? EventClass { get; init; }

    [JsonPropertyName("status")]
    public required int Status { get; init; }

    [JsonPropertyName("duration_ns")]
    public required long DurationNs { get; init; }

    [JsonPropertyName("sdk_version")]
    public required string SdkVersion { get; init; }

    [JsonPropertyName("event_attrs")]
    public Dictionary<string, object>? EventAttrs { get; init; }

    [JsonPropertyName("detail")]
    public CeDetail? Detail { get; init; }

    [JsonPropertyName("captured_before_alert")]
    public bool? CapturedBeforeAlert { get; init; }

    [JsonPropertyName("ring_buffer_seq")]
    public long? RingBufferSeq { get; init; }
}
