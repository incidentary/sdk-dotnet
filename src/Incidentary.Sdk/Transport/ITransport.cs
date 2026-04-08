using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Transport;

/// <summary>
/// Abstraction for uploading causal event batches and notifying the Incidentary backend.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Uploads a batch of causal events to the ingest endpoint.
    /// Returns true on success, false on failure. Never throws.
    /// </summary>
    Task<bool> UploadBatchAsync(
        IReadOnlyList<CausalEvent> events,
        string captureMode,
        string? incidentId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a service lifecycle event to the backend (fire-and-forget).
    /// Never throws.
    /// </summary>
    Task NotifyBackendAsync(
        string eventType,
        string serviceId,
        IDictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// True if the transport is able to send (circuit closed, quota not paused, base URL configured).
    /// </summary>
    bool IsHealthy { get; }
}
