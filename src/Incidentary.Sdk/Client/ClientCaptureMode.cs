namespace Incidentary.Sdk;

/// <summary>
/// The runtime capture mode of the Incidentary client.
/// Controls how much detail is captured and sent to the backend.
/// </summary>
public enum ClientCaptureMode
{
    /// <summary>Normal operation. Only skeleton causal events are captured.</summary>
    Normal,

    /// <summary>Pre-armed state. The SDK detected anomalous conditions locally and is capturing full detail.</summary>
    PreArmed,

    /// <summary>Active incident. Full detail capture continues until the incident is closed.</summary>
    Incident
}
