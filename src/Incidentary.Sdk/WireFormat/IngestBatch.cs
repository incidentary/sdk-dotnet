using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>The batch envelope sent to <c>POST /api/v1/ingest/batch</c>.</summary>
public sealed class IngestBatch
{
    [JsonPropertyName("schema_version")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("workspace_id")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("service_id")]
    public required string ServiceId { get; init; }

    [JsonPropertyName("environment")]
    public required string Environment { get; init; }

    [JsonPropertyName("flushed_at")]
    public required long FlushedAt { get; init; }

    [JsonPropertyName("capture_mode")]
    public required string CaptureMode { get; init; }

    [JsonPropertyName("events")]
    public required IReadOnlyList<CausalEvent> Events { get; init; }

    [JsonPropertyName("deploy_id")]
    public string? DeployId { get; init; }

    [JsonPropertyName("git_sha")]
    public string? GitSha { get; init; }

    [JsonPropertyName("sdk_telemetry")]
    public SdkTelemetry? SdkTelemetry { get; init; }
}
