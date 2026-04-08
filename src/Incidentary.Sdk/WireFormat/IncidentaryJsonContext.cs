using System.Text.Json;
using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(IngestBatch))]
[JsonSerializable(typeof(IngestResponse))]
[JsonSerializable(typeof(CausalEvent))]
[JsonSerializable(typeof(CeDetail))]
[JsonSerializable(typeof(RetryDetail))]
[JsonSerializable(typeof(DownstreamDetail))]
[JsonSerializable(typeof(SdkTelemetry))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class IncidentaryJsonContext : JsonSerializerContext { }
