using Incidentary.Sdk.DownstreamEdge;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk;

/// <summary>Options for recording a completed request.</summary>
public sealed class RecordRequestOptions
{
    /// <summary>The causal event kind. Defaults to HTTP_IN.</summary>
    public CeKind Kind { get; init; } = CeKind.HttpIn;

    /// <summary>Override event type (e.g., "webhook_in" instead of "http_in").</summary>
    public string? EventType { get; init; }

    /// <summary>Duration in nanoseconds. If null, duration is recorded as 0. Use the ASP.NET Core middleware or set this value explicitly for accurate timing.</summary>
    public long? DurationNs { get; init; }

    /// <summary>Trace context for this request. If null, uses ambient context.</summary>
    public string? TraceId { get; init; }

    /// <summary>Parent causal event ID.</summary>
    public string? ParentCeId { get; init; }

    /// <summary>Custom event attributes (max 32 keys, primitives only).</summary>
    public Dictionary<string, object>? EventAttrs { get; init; }

    /// <summary>HTTP method (for detail capture).</summary>
    public string? Method { get; init; }

    /// <summary>Route template (for detail capture, e.g. "/orders/:id/checkout").</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>Request body size in bytes.</summary>
    public long? RequestBytes { get; init; }

    /// <summary>Response body size in bytes.</summary>
    public long? ResponseBytes { get; init; }

    /// <summary>Filtered request headers (for detail capture).</summary>
    public Dictionary<string, string>? RequestHeaders { get; init; }

    /// <summary>Filtered response headers (for detail capture).</summary>
    public Dictionary<string, string>? ResponseHeaders { get; init; }

    /// <summary>Whether the request was cancelled by the client.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Whether the request timed out.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Outbound retry metadata (for HTTP_OUT events).</summary>
    public OutboundRetryMetadata? RetryMetadata { get; init; }
}
