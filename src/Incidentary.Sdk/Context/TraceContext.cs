namespace Incidentary.Sdk.Context;

/// <summary>Holds the trace ID and causal event ID for the current operation.</summary>
public readonly record struct TraceContext(string TraceId, string CeId)
{
    /// <summary>Creates a new root context with a fresh trace ID and CE ID.</summary>
    public static TraceContext NewRoot() => new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

    /// <summary>Creates a child context with the same trace ID but a new CE ID.</summary>
    public TraceContext NewChild() => new(TraceId, Guid.NewGuid().ToString());
}
