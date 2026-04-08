namespace Incidentary.Sdk.DownstreamEdge;

/// <summary>Result of downstream edge key resolution.</summary>
public sealed class EdgeKeyResolution
{
    /// <summary>The resolved edge key.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Quality level of the resolution. One of
    /// <see cref="RetryKeyQualities.Explicit"/>,
    /// <see cref="RetryKeyQualities.RouteTemplate"/>,
    /// <see cref="RetryKeyQualities.LogicalEdge"/>,
    /// <see cref="RetryKeyQualities.NormalizedUrl"/>, or
    /// <see cref="RetryKeyQualities.Unknown"/>.
    /// </summary>
    public required string Quality { get; init; }
}

/// <summary>Constants for edge key resolution quality levels.</summary>
public static class RetryKeyQualities
{
    /// <summary>Caller provided an explicit retry/idempotency/operation key.</summary>
    public const string Explicit = "explicit";

    /// <summary>Key derived from a route template.</summary>
    public const string RouteTemplate = "route_template";

    /// <summary>Key derived from logical edge (service + operation).</summary>
    public const string LogicalEdge = "logical_edge";

    /// <summary>Key derived from a normalized URL.</summary>
    public const string NormalizedUrl = "normalized_url";

    /// <summary>No key could be resolved.</summary>
    public const string Unknown = "unknown";
}
