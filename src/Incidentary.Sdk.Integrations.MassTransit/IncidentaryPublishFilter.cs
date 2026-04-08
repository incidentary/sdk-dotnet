using System.Diagnostics;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;
using MassTransit;

namespace Incidentary.Sdk.Integrations.MassTransit;

/// <summary>MassTransit publish filter that records queue_publish events and injects trace context.</summary>
public sealed class IncidentaryPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly IIncidentaryClient _client;

    public IncidentaryPublishFilter(IIncidentaryClient client)
    {
        _client = client;
    }

    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        var current = IncidentaryActivity.Current;
        if (current.HasValue)
        {
            context.Headers.Set(HeaderConstants.TraceIdHeader, current.Value.TraceId);
            context.Headers.Set(HeaderConstants.ParentCeHeader, current.Value.CeId);
        }

        var sw = Stopwatch.StartNew();
        var status = 200;

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
            _client.RecordQueuePublish(new RecordEventOptions
            {
                Kind = CeKind.QueuePublish,
                Status = status,
                DurationNs = sw.Elapsed.Ticks * 100,
                Topic = typeof(T).Name
            });
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("incidentary-publish");
    }
}
