namespace Incidentary.Sdk;

/// <summary>Snapshot of the pre-arm engine state for observability and debugging.</summary>
public sealed class PreArmDebugState
{
    /// <summary>Current capture mode.</summary>
    public required ClientCaptureMode Mode { get; init; }

    /// <summary>Named counters for pre-arm activity (e.g., prearm_enter_total, prearm_exit_total).</summary>
    public required IReadOnlyDictionary<string, long> Counters { get; init; }

    /// <summary>Active pre-arm window, if any.</summary>
    public PreArmWindowInfo? ActiveWindow { get; init; }

    /// <summary>Recent pre-arm windows (up to last 5).</summary>
    public IReadOnlyList<PreArmWindowInfo>? RecentWindows { get; init; }

    /// <summary>Active trigger reasons, if currently pre-armed.</summary>
    public IReadOnlyList<string>? ActiveTriggers { get; init; }
}

/// <summary>Information about a pre-arm window.</summary>
public sealed class PreArmWindowInfo
{
    /// <summary>When this window started (milliseconds since process start, monotonic — <see cref="Environment.TickCount64"/>).</summary>
    public required long StartedAtTicks { get; init; }

    /// <summary>When this window ended (milliseconds since process start, monotonic), or null if still active.</summary>
    public long? EndedAtTicks { get; init; }

    /// <summary>What triggered this window.</summary>
    public required IReadOnlyList<string> TriggerReasons { get; init; }

    /// <summary>Why this window closed.</summary>
    public string? CloseReason { get; init; }

    /// <summary>Incident ID if this window was escalated.</summary>
    public string? BoundIncidentId { get; init; }
}
