namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Immutable result of a single trigger evaluation.
/// </summary>
internal sealed class TriggerResult
{
    /// <summary>Shared instance representing no anomaly.</summary>
    public static readonly TriggerResult None = new() { Severity = TriggerSeverity.None };

    /// <summary>Severity of the detected anomaly.</summary>
    public required TriggerSeverity Severity { get; init; }

    /// <summary>Machine-readable trigger type identifier (e.g., "error_rate", "slow_success").</summary>
    public string? TriggerType { get; init; }

    /// <summary>Human-readable reason string for diagnostics.</summary>
    public string? Reason { get; init; }
}
