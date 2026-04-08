using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Integrations.Grpc;

/// <summary>gRPC client interceptor for recording grpc_out events.</summary>
public sealed class IncidentaryClientInterceptor : Interceptor
{
    private readonly IIncidentaryClient _client;

    public IncidentaryClientInterceptor(IIncidentaryClient client)
    {
        _client = client;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var current = IncidentaryActivity.Current;
        var traceId = current?.TraceId ?? Guid.NewGuid().ToString();
        var childCeId = Guid.NewGuid().ToString();

        // Inject headers
        var headers = context.Options.Headers ?? new Metadata();
        headers.Add(HeaderConstants.TraceIdHeader, traceId);
        headers.Add(HeaderConstants.ParentCeHeader, childCeId);

        var newOptions = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, newOptions);

        _client.RecordRequestStart(CeKind.HttpOut);
        var sw = Stopwatch.StartNew();

        var call = continuation(request, newContext);

        // Wrap the response to record completion
        var responseAsync = WrapResponse(call.ResponseAsync, sw, traceId, current?.CeId, context.Method.FullName);

        return new AsyncUnaryCall<TResponse>(
            responseAsync,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private async Task<TResponse> WrapResponse<TResponse>(
        Task<TResponse> responseTask,
        Stopwatch sw,
        string traceId,
        string? parentCeId,
        string method)
    {
        int status = 200;
        try
        {
            var response = await responseTask.ConfigureAwait(false);
            return response;
        }
        catch (RpcException ex)
        {
            status = MapGrpcStatus(ex.StatusCode);
            throw;
        }
        finally
        {
            sw.Stop();
            _client.RecordRequest(status, new RecordRequestOptions
            {
                Kind = CeKind.HttpOut,
                EventType = EventTypes.GrpcOut,
                DurationNs = sw.Elapsed.Ticks * 100,
                TraceId = traceId,
                ParentCeId = parentCeId,
                Method = method
            });
        }
    }

    private static int MapGrpcStatus(StatusCode code) => code switch
    {
        StatusCode.OK => 200,
        StatusCode.InvalidArgument => 400,
        StatusCode.NotFound => 404,
        StatusCode.PermissionDenied => 403,
        StatusCode.Unauthenticated => 401,
        StatusCode.ResourceExhausted => 429,
        StatusCode.Unavailable => 503,
        StatusCode.DeadlineExceeded => 504,
        _ => 500
    };
}
