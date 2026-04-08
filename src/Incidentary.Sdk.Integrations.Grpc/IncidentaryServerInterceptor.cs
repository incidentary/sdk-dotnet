using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Integrations.Grpc;

/// <summary>gRPC server interceptor for recording grpc_in events.</summary>
public sealed class IncidentaryServerInterceptor : Interceptor
{
    private readonly IIncidentaryClient _client;

    public IncidentaryServerInterceptor(IIncidentaryClient client)
    {
        _client = client;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var traceId = GetMetadataValue(context.RequestHeaders, HeaderConstants.TraceIdHeader)
            ?? Guid.NewGuid().ToString();
        var parentCeId = GetMetadataValue(context.RequestHeaders, HeaderConstants.ParentCeHeader);
        var ceId = Guid.NewGuid().ToString();

        using var scope = IncidentaryActivity.SetContext(new TraceContext(traceId, ceId));
        _client.RecordRequestStart(CeKind.HttpIn);
        var sw = Stopwatch.StartNew();
        int status = 200;

        try
        {
            var response = await continuation(request, context).ConfigureAwait(false);
            return response;
        }
        catch (RpcException ex)
        {
            status = MapGrpcStatus(ex.StatusCode);
            throw;
        }
        catch
        {
            status = 500;
            throw;
        }
        finally
        {
            sw.Stop();
            _client.RecordRequest(status, new RecordRequestOptions
            {
                Kind = CeKind.HttpIn,
                EventType = EventTypes.GrpcIn,
                DurationNs = sw.Elapsed.Ticks * 100,
                TraceId = traceId,
                ParentCeId = parentCeId,
                Method = context.Method
            });
        }
    }

    private static string? GetMetadataValue(Metadata headers, string key)
    {
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return null;
    }

    private static int MapGrpcStatus(StatusCode code) => code switch
    {
        StatusCode.OK => 200,
        StatusCode.InvalidArgument => 400,
        StatusCode.NotFound => 404,
        StatusCode.PermissionDenied => 403,
        StatusCode.Unauthenticated => 401,
        StatusCode.Unavailable => 503,
        StatusCode.DeadlineExceeded => 504,
        StatusCode.Internal => 500,
        _ => 500
    };
}
