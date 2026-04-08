using System.Text.Json;
using System.Text.Json.Serialization;

namespace Incidentary.Sdk.WireFormat;

/// <summary>Operation kind for a causal event.</summary>
[JsonConverter(typeof(CeKindConverter))]
public enum CeKind
{
    HttpIn,
    HttpOut,
    QueuePublish,
    QueueConsume,
    Internal
}

/// <summary>
/// Serializes <see cref="CeKind"/> to the wire format strings
/// (<c>HTTP_IN</c>, <c>HTTP_OUT</c>, etc.).
/// </summary>
public sealed class CeKindConverter : JsonConverter<CeKind>
{
    private const string HttpInWire = "HTTP_IN";
    private const string HttpOutWire = "HTTP_OUT";
    private const string QueuePublishWire = "QUEUE_PUBLISH";
    private const string QueueConsumeWire = "QUEUE_CONSUME";
    private const string InternalWire = "INTERNAL";

    public override CeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            HttpInWire => CeKind.HttpIn,
            HttpOutWire => CeKind.HttpOut,
            QueuePublishWire => CeKind.QueuePublish,
            QueueConsumeWire => CeKind.QueueConsume,
            InternalWire => CeKind.Internal,
            _ => throw new JsonException($"Unknown CeKind value: '{value}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, CeKind value, JsonSerializerOptions options)
    {
        var wire = value switch
        {
            CeKind.HttpIn => HttpInWire,
            CeKind.HttpOut => HttpOutWire,
            CeKind.QueuePublish => QueuePublishWire,
            CeKind.QueueConsume => QueueConsumeWire,
            CeKind.Internal => InternalWire,
            _ => throw new JsonException($"Unknown CeKind value: '{value}'")
        };
        writer.WriteStringValue(wire);
    }
}
