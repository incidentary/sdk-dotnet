namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Severity level produced by a pre-arm trigger evaluation.
/// </summary>
internal enum TriggerSeverity
{
    /// <summary>No anomaly detected.</summary>
    None,

    /// <summary>Mild anomaly — used for hysteresis / exit detection.</summary>
    Mild,

    /// <summary>Severe anomaly — sufficient to enter PRE_ARMED state.</summary>
    Severe
}
