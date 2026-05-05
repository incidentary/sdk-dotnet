using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>The batch envelope sent to <c>POST /api/v2/ingest</c>.</summary>
public sealed class IngestBatch
{
    [JsonPropertyName("specversion")]
    public required string Specversion { get; init; }

    [JsonPropertyName("resource")]
    public required IngestResource Resource { get; init; }

    [JsonPropertyName("agent")]
    public required IngestAgent Agent { get; init; }

    [JsonPropertyName("flushed_at")]
    public required long FlushedAt { get; init; }

    [JsonPropertyName("capture_mode")]
    public required string CaptureMode { get; init; }

    [JsonPropertyName("events")]
    public required IReadOnlyList<CausalEvent> Events { get; init; }
}

/// <summary>Resource block identifying the service and environment.</summary>
public sealed class IngestResource
{
    [JsonPropertyName("workspace_id")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("service_id")]
    public required string ServiceId { get; init; }

    [JsonPropertyName("environment")]
    public required string Environment { get; init; }

    [JsonPropertyName("deploy_id")]
    public string? DeployId { get; init; }

    [JsonPropertyName("git_sha")]
    public string? GitSha { get; init; }
}

/// <summary>Agent block identifying the SDK that produced this batch.</summary>
public sealed class IngestAgent
{
    [JsonPropertyName("sdk_version")]
    public required string SdkVersion { get; init; }

    [JsonPropertyName("sdk_language")]
    public string SdkLanguage { get; init; } = "dotnet";

    [JsonPropertyName("queue_depth")]
    public long QueueDepth { get; init; }

    [JsonPropertyName("dropped_ce_count")]
    public long DroppedCeCount { get; init; }

    [JsonPropertyName("flush_latency_ms")]
    public long FlushLatencyMs { get; init; }

    [JsonPropertyName("flush_latency_ema_ms")]
    public double FlushLatencyEmaMs { get; init; }

    [JsonPropertyName("current_batch_size")]
    public int CurrentBatchSize { get; init; }
}
