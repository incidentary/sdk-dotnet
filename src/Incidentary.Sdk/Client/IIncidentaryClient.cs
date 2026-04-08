using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk;

/// <summary>
/// The core Incidentary SDK client interface.
/// All methods are fail-open — they never throw into user code.
/// Thread-safe for concurrent use.
/// </summary>
public interface IIncidentaryClient : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the current capture mode (Normal, PreArmed, or Incident).</summary>
    ClientCaptureMode CaptureMode { get; }

    /// <summary>Returns true when the SDK should capture detailed metadata for the current event.</summary>
    bool ShouldCaptureDetail { get; }

    /// <summary>Records a completed inbound request. Call after the response status code is known.</summary>
    void RecordRequest(int statusCode, RecordRequestOptions? options = null);

    /// <summary>Records the start of an inbound request (increments in-flight counter).</summary>
    void RecordRequestStart(CeKind kind = CeKind.HttpIn);

    /// <summary>Records a generic event with the specified type.</summary>
    void RecordEvent(string eventType, RecordEventOptions? options = null);

    /// <summary>Records a queue publish event.</summary>
    void RecordQueuePublish(RecordEventOptions? options = null);

    /// <summary>Records a queue consume event.</summary>
    void RecordQueueConsume(RecordEventOptions? options = null);

    /// <summary>Records a job start event.</summary>
    void RecordJobStart(RecordEventOptions? options = null);

    /// <summary>Records a job end event.</summary>
    void RecordJobEnd(RecordEventOptions? options = null);

    /// <summary>Records an inbound webhook event.</summary>
    void RecordWebhookIn(RecordEventOptions? options = null);

    /// <summary>Records an outbound webhook event.</summary>
    void RecordWebhookOut(RecordEventOptions? options = null);

    /// <summary>Writes a pre-constructed causal event to the ring buffer.</summary>
    void WriteEvent(CausalEvent ce);

    /// <summary>Flushes buffered events to the backend. Fire-and-forget.</summary>
    Task FlushToBackendAsync(string? incidentId = null, CancellationToken ct = default);

    /// <summary>Escalates the current state to INCIDENT mode.</summary>
    void EscalateToIncident(string? incidentId = null);

    /// <summary>Closes the current incident and returns to NORMAL mode.</summary>
    void CloseIncident();

    /// <summary>Gets a snapshot of the pre-arm engine debug state for observability.</summary>
    PreArmDebugState GetPreArmDebugState();
}
