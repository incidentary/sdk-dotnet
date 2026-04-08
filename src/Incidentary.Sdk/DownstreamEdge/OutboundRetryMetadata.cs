namespace Incidentary.Sdk.DownstreamEdge;

/// <summary>Metadata provided by the caller to help identify retry groups.</summary>
public sealed class OutboundRetryMetadata
{
    // Level 1: Explicit identifiers
    /// <summary>Explicit retry group identifier assigned by the caller.</summary>
    public string? RetryGroupId { get; init; }

    /// <summary>Idempotency key for the outbound request.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Logical operation key (e.g. "CreateOrder").</summary>
    public string? OperationKey { get; init; }

    /// <summary>Explicit retry key assigned by the caller.</summary>
    public string? RetryKey { get; init; }

    // Level 2: Route template
    /// <summary>Route template such as "/orders/{id}/items".</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>A pre-computed route key.</summary>
    public string? RouteKey { get; init; }

    // Level 3: Logical edge
    /// <summary>Name of the downstream service being called.</summary>
    public string? DownstreamService { get; init; }

    /// <summary>A pre-computed edge key.</summary>
    public string? EdgeKey { get; init; }

    /// <summary>Logical operation name on the downstream service.</summary>
    public string? OperationName { get; init; }

    // Explicit retry attempt number
    /// <summary>Current retry attempt number (0 = first attempt).</summary>
    public int? RetryAttempt { get; init; }
}
