using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>Response from the ingest endpoint.</summary>
public sealed class IngestResponse
{
    [JsonPropertyName("accepted")]
    public int Accepted { get; init; }

    [JsonPropertyName("dropped")]
    public int Dropped { get; init; }

    [JsonPropertyName("drop_reasons")]
    public Dictionary<string, int>? DropReasons { get; init; }
}
