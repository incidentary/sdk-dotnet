namespace Incidentary.Sdk;

/// <summary>
/// Configuration options for the Incidentary client.
/// All pre-arm thresholds match defaults across the Node, Go, and Python SDKs.
/// </summary>
public sealed class IncidentaryClientOptions
{
    // ─── Required ───────────────────────────────────────────────────────

    /// <summary>Workspace API key for authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Service name that identifies this service in the causal graph.</summary>
    public string ServiceName { get; set; } = string.Empty;

    // ─── Connection ─────────────────────────────────────────────────────

    /// <summary>Incidentary backend base URL (e.g., "https://api.incidentary.io").</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Workspace identifier for ingest validation.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Environment label (e.g., "production", "staging").</summary>
    public string Environment { get; set; } = "production";

    /// <summary>Deployment identifier (e.g., container image tag).</summary>
    public string? DeployId { get; set; }

    /// <summary>Git commit SHA of the deployed code.</summary>
    public string? GitSha { get; set; }

    /// <summary>HTTP request timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 5_000;

    // ─── Buffer ─────────────────────────────────────────────────────────

    /// <summary>Ring buffer capacity (number of events).</summary>
    public int BufferCapacity { get; set; } = 4_000;

    // ─── Pre-arm: Error Rate (5xx) ──────────────────────────────────────

    /// <summary>5xx error rate percentage to enter PRE_ARMED.</summary>
    public double PreArmThresholdHigh { get; set; } = 10.0;

    /// <summary>5xx error rate percentage to exit PRE_ARMED.</summary>
    public double PreArmThresholdLow { get; set; } = 2.0;

    /// <summary>Minimum duration in PRE_ARMED state (ms).</summary>
    public int PreArmMinDurationMs { get; set; } = 60_000;

    /// <summary>Maximum time in PRE_ARMED before automatic exit (ms).</summary>
    public int PreArmTtlMs { get; set; } = 300_000;

    /// <summary>Cooldown after exiting PRE_ARMED before re-entry is allowed (ms).</summary>
    public int PreArmCooldownMs { get; set; } = 30_000;

    // ─── Pre-arm: Slow Success ──────────────────────────────────────────

    /// <summary>Enable the slow-success anomaly trigger.</summary>
    public bool PreArmEnableSlowSuccess { get; set; } = true;

    /// <summary>Minimum absolute latency (ms) to consider a request slow.</summary>
    public int PreArmSlowMinMs { get; set; } = 250;

    /// <summary>Multiplier over EWMA baseline to flag a request as slow.</summary>
    public double PreArmSlowMultiplier { get; set; } = 2.0;

    /// <summary>EWMA smoothing factor for latency baseline.</summary>
    public double PreArmSlowAlpha { get; set; } = 0.1;

    /// <summary>Slow-success rate to trigger severe pre-arm.</summary>
    public double PreArmSlowSuccessRateHigh { get; set; } = 0.20;

    /// <summary>Slow-success rate to trigger mild pre-arm.</summary>
    public double PreArmSlowSuccessRateMild { get; set; } = 0.10;

    /// <summary>Minimum request samples before slow-success trigger activates.</summary>
    public int PreArmSlowMinSamples { get; set; } = 50;

    /// <summary>Count 4xx responses as success-like for slow-success calculation.</summary>
    public bool PreArmSlowInclude4xxAsSuccessLike { get; set; } = true;

    // ─── Pre-arm: In-Flight Pileup ─────────────────────────────────────

    /// <summary>Enable the in-flight pileup anomaly trigger.</summary>
    public bool PreArmEnableInFlight { get; set; } = true;

    /// <summary>Minimum absolute in-flight count to trigger.</summary>
    public int PreArmInFlightMinAbs { get; set; } = 32;

    /// <summary>Multiplier over baseline to flag in-flight pileup.</summary>
    public double PreArmInFlightMultiplier { get; set; } = 2.0;

    /// <summary>Minimum net growth in in-flight count to trigger.</summary>
    public int PreArmInFlightNetGrowthMin { get; set; } = 16;

    /// <summary>Hold duration (seconds) for severe in-flight pileup trigger.</summary>
    public int PreArmInFlightHoldSecs { get; set; } = 3;

    /// <summary>Hold duration (seconds) for mild in-flight pileup trigger.</summary>
    public int PreArmInFlightMildHoldSecs { get; set; } = 2;

    // ─── Pre-arm: Retry Onset ───────────────────────────────────────────

    /// <summary>Enable the retry onset anomaly trigger.</summary>
    public bool PreArmEnableRetry { get; set; } = true;

    /// <summary>Sliding window for retry rate calculation (ms).</summary>
    public int PreArmRetryWindowMs { get; set; } = 5_000;

    /// <summary>Retry rate to trigger severe pre-arm.</summary>
    public double PreArmRetryRateHigh { get; set; } = 0.10;

    /// <summary>Retry rate to trigger mild pre-arm.</summary>
    public double PreArmRetryRateMild { get; set; } = 0.05;

    /// <summary>Minimum total events in window before retry trigger activates.</summary>
    public int PreArmRetryMinTotal { get; set; } = 20;

    /// <summary>Hash table size for retry key deduplication.</summary>
    public int PreArmRetryTableSize { get; set; } = 4_096;

    // ─── Detail Capture ─────────────────────────────────────────────────

    /// <summary>Enable detail capture in PRE_ARMED/INCIDENT modes.</summary>
    public bool DetailCaptureEnabled { get; set; } = true;

    /// <summary>Enable payload snippet capture in detail.</summary>
    public bool DetailPayloadEnabled { get; set; }

    /// <summary>Maximum payload snippet size in bytes.</summary>
    public int DetailMaxPayloadBytes { get; set; } = 4_096;

    /// <summary>Request headers to include in detail (case-insensitive).</summary>
    public IReadOnlyList<string>? RequestHeaderAllowlist { get; set; }

    /// <summary>Response headers to include in detail (case-insensitive).</summary>
    public IReadOnlyList<string>? ResponseHeaderAllowlist { get; set; }

    /// <summary>JSON field names to redact in payload snippets.</summary>
    public IReadOnlyList<string>? RedactFields { get; set; }

    // ─── Instrumentation ────────────────────────────────────────────────

    /// <summary>
    /// Automatically discover and instrument available libraries at client construction time.
    /// When enabled, the SDK scans for registered <see cref="Incidentary.Sdk.Integrations.IIntegration"/>
    /// implementations and calls <c>Setup</c> on each one that reports <c>IsAvailable() == true</c>.
    /// Set to <c>false</c> to disable all auto-instrumentation and register integrations manually.
    /// </summary>
    public bool AutoInstrument { get; set; } = true;

    // ─── Callbacks ──────────────────────────────────────────────────────

    /// <summary>Called when the SDK encounters an internal error. Never throws into user code.</summary>
    public Action<Exception>? OnError { get; set; }

    /// <summary>
    /// Default request headers captured in detail mode.
    /// Combine with your own: <c>options.RequestHeaderAllowlist = [..DefaultRequestHeaderAllowlist, "x-my-header"];</c>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultRequestHeaderAllowlist =
        ["content-type", "content-length", "user-agent", "x-request-id", "accept"];

    /// <summary>
    /// Default response headers captured in detail mode.
    /// Combine with your own: <c>options.ResponseHeaderAllowlist = [..DefaultResponseHeaderAllowlist, "x-my-header"];</c>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultResponseHeaderAllowlist =
        ["content-type", "content-length", "x-request-id"];
}
