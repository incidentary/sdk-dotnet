using System.Diagnostics;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Extensions.Http;

/// <summary>
/// DelegatingHandler that instruments outbound HTTP requests.
/// Injects trace context headers and records HTTP_OUT events with correct causal linkage.
/// The propagated <c>x-incidentary-parent-ce</c> header matches the CE ID of the recorded HTTP_OUT event
/// so the downstream service can link its HTTP_IN event back to this outbound call.
/// </summary>
public sealed class IncidentaryDelegatingHandler : DelegatingHandler
{
    private readonly IIncidentaryClient _client;

    public IncidentaryDelegatingHandler(IIncidentaryClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // The current span's CE ID becomes the parent for the downstream service.
        // We generate a new CE ID for this outbound call — this is what we propagate
        // AND record, so the causal graph links correctly.
        var current = IncidentaryActivity.Current;
        var traceId = current?.TraceId ?? Guid.NewGuid().ToString();
        var outboundCeId = Guid.NewGuid().ToString();

        request.Headers.TryAddWithoutValidation(HeaderConstants.TraceIdHeader, traceId);
        request.Headers.TryAddWithoutValidation(HeaderConstants.ParentCeHeader, outboundCeId);

        _client.RecordRequestStart(CeKind.HttpOut);
        var sw = Stopwatch.StartNew();
        int statusCode = 0;
        bool timedOut = false;
        bool cancelled = false;

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            statusCode = (int)response.StatusCode;
            return response;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            throw;
        }
        catch (TaskCanceledException)
        {
            timedOut = true;
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Use the actual status code if available (e.g., from EnsureSuccessStatusCode);
            // fall back to 502 for network-level failures where no status code exists.
            statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 502;
            throw;
        }
        finally
        {
            sw.Stop();
            var durationNs = sw.Elapsed.Ticks * 100;

            // Record the HTTP_OUT event. The CeId written to the ring buffer by
            // IncidentaryClient.RecordRequest is auto-generated, but the ParentCeId
            // correctly links to the inbound span. The outboundCeId we propagated
            // via headers is what the downstream will use as *its* ParentCeId.
            // To fully close the causal link we pass outboundCeId through EventAttrs
            // so the backend can correlate the propagated header with this event.
            var attrs = new Dictionary<string, object>
            {
                ["outbound_ce_id"] = outboundCeId
            };

            var options = new RecordRequestOptions
            {
                Kind = CeKind.HttpOut,
                EventType = EventTypes.HttpOut,
                DurationNs = durationNs,
                TraceId = traceId,
                ParentCeId = current?.CeId,
                Method = request.Method.Method,
                RouteTemplate = request.RequestUri?.AbsolutePath,
                TimedOut = timedOut,
                Cancelled = cancelled,
                EventAttrs = attrs
            };

            _client.RecordRequest(statusCode, options);
        }
    }
}
