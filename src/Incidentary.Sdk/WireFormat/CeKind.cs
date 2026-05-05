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
    Internal,
    DbQuery,
    DbConnect,
    RpcServer,
    RpcClient,
    Job
}

/// <summary>
/// Serializes <see cref="CeKind"/> to the wire format strings
/// (<c>HTTP_SERVER</c>, <c>HTTP_CLIENT</c>, etc.).
/// </summary>
public sealed class CeKindConverter : JsonConverter<CeKind>
{
    private const string HttpServerWire = "HTTP_SERVER";
    private const string HttpClientWire = "HTTP_CLIENT";
    private const string QueuePublishWire = "QUEUE_PUBLISH";
    private const string QueueConsumeWire = "QUEUE_CONSUME";
    private const string InternalWire = "INTERNAL";
    private const string DbQueryWire = "DB_QUERY";
    private const string DbConnectWire = "DB_CONNECT";
    private const string RpcServerWire = "RPC_SERVER";
    private const string RpcClientWire = "RPC_CLIENT";
    private const string JobWire = "JOB";

    public override CeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            HttpServerWire => CeKind.HttpIn,
            HttpClientWire => CeKind.HttpOut,
            QueuePublishWire => CeKind.QueuePublish,
            QueueConsumeWire => CeKind.QueueConsume,
            InternalWire => CeKind.Internal,
            DbQueryWire => CeKind.DbQuery,
            DbConnectWire => CeKind.DbConnect,
            RpcServerWire => CeKind.RpcServer,
            RpcClientWire => CeKind.RpcClient,
            JobWire => CeKind.Job,
            _ => throw new JsonException($"Unknown CeKind value: '{value}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, CeKind value, JsonSerializerOptions options)
    {
        var wire = value switch
        {
            CeKind.HttpIn => HttpServerWire,
            CeKind.HttpOut => HttpClientWire,
            CeKind.QueuePublish => QueuePublishWire,
            CeKind.QueueConsume => QueueConsumeWire,
            CeKind.Internal => InternalWire,
            CeKind.DbQuery => DbQueryWire,
            CeKind.DbConnect => DbConnectWire,
            CeKind.RpcServer => RpcServerWire,
            CeKind.RpcClient => RpcClientWire,
            CeKind.Job => JobWire,
            _ => throw new JsonException($"Unknown CeKind value: '{value}'")
        };
        writer.WriteStringValue(wire);
    }
}
