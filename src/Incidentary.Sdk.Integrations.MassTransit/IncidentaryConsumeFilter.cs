using System.Diagnostics;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;
using MassTransit;

namespace Incidentary.Sdk.Integrations.MassTransit;

/// <summary>MassTransit consume filter that records queue_consume events and extracts trace context.</summary>
public sealed class IncidentaryConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly IIncidentaryClient _client;

    public IncidentaryConsumeFilter(IIncidentaryClient client)
    {
        _client = client;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var traceId = context.Headers.Get<string>(HeaderConstants.TraceIdHeader)
            ?? Guid.NewGuid().ToString();
        var parentCeId = context.Headers.Get<string>(HeaderConstants.ParentCeHeader);
        var ceId = Guid.NewGuid().ToString();

        using var scope = IncidentaryActivity.SetContext(new TraceContext(traceId, ceId));

        var sw = Stopwatch.StartNew();
        int status = 200;

        try
        {
            await next.Send(context).ConfigureAwait(false);
        }
        catch
        {
            status = 500;
            throw;
        }
        finally
        {
            sw.Stop();
            _client.RecordQueueConsume(new RecordEventOptions
            {
                Kind = CeKind.QueueConsume,
                Status = status,
                DurationNs = sw.Elapsed.Ticks * 100,
                TraceId = traceId,
                ParentCeId = parentCeId,
                Topic = typeof(T).Name
            });
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("incidentary-consume");
    }
}
