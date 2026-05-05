namespace Incidentary.Sdk.Transport;

/// <summary>
/// Result of a single batch upload to the backend.
/// </summary>
public sealed record FlushResult
{
    /// <summary>True if the batch was accepted (2xx response).</summary>
    public bool Success { get; init; }

    /// <summary>
    /// The capture mode the backend is requesting the SDK to switch to.
    /// Null when no header is present; "FULL" when the backend wants full capture.
    /// Read from the <c>X-Capture-Mode-Requested</c> response header.
    /// </summary>
    public string? RequestedCaptureMode { get; init; }

    /// <summary>A failed result with no requested capture mode.</summary>
    public static readonly FlushResult Failed = new() { Success = false };
}
