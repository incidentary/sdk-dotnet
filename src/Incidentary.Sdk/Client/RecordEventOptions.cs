using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk;

/// <summary>Options for recording a generic event.</summary>
public sealed class RecordEventOptions
{
    /// <summary>The causal event kind. Defaults to Internal.</summary>
    public CeKind Kind { get; init; } = CeKind.Internal;

    /// <summary>HTTP-equivalent status code (200 for success, 500 for failure, 0 for N/A).</summary>
    public int Status { get; init; } = 200;

    /// <summary>Duration in nanoseconds.</summary>
    public long DurationNs { get; init; }

    /// <summary>Trace context for this event. If null, uses ambient context.</summary>
    public string? TraceId { get; init; }

    /// <summary>Parent causal event ID.</summary>
    public string? ParentCeId { get; init; }

    /// <summary>Custom event attributes (max 32 keys, primitives only).</summary>
    public Dictionary<string, object>? EventAttrs { get; init; }

    /// <summary>Optional topic name (for queue events).</summary>
    public string? Topic { get; init; }
}
