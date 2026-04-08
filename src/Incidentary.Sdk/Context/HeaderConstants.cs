namespace Incidentary.Sdk.Context;

/// <summary>Standard Incidentary HTTP headers for trace propagation.</summary>
public static class HeaderConstants
{
    /// <summary>Header name for the trace ID.</summary>
    public const string TraceIdHeader = "x-incidentary-trace-id";

    /// <summary>Header name for the parent causal event ID.</summary>
    public const string ParentCeHeader = "x-incidentary-parent-ce";
}
