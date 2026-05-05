using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>A causal event on the wire (V2 format).</summary>
public sealed class CausalEvent
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("trace_id")]
    public required string TraceId { get; init; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("span_id")]
    public string? SpanId { get; init; }

    [JsonPropertyName("service_id")]
    public required string ServiceId { get; init; }

    [JsonPropertyName("occurred_at")]
    public required long OccurredAt { get; init; }

    [JsonPropertyName("kind")]
    public required CeKind Kind { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status_code")]
    public required int StatusCode { get; init; }

    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    [JsonPropertyName("duration_ns")]
    public required long DurationNs { get; init; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, object>? Attributes { get; init; }

    [JsonPropertyName("detail")]
    public CeDetail? Detail { get; init; }

    [JsonPropertyName("captured_before_alert")]
    public bool? CapturedBeforeAlert { get; init; }

    [JsonPropertyName("ring_buffer_seq")]
    public long? RingBufferSeq { get; init; }
}
