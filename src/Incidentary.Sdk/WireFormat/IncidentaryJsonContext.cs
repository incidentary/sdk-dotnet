using System.Text.Json;
using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(IngestBatch))]
[JsonSerializable(typeof(IngestResponse))]
[JsonSerializable(typeof(IngestResource))]
[JsonSerializable(typeof(IngestAgent))]
[JsonSerializable(typeof(CausalEvent))]
[JsonSerializable(typeof(CeDetail))]
[JsonSerializable(typeof(RetryDetail))]
[JsonSerializable(typeof(DownstreamDetail))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal partial class IncidentaryJsonContext : JsonSerializerContext { }
