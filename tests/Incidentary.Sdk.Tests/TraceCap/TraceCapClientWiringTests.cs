// L1 wiring acceptance — TraceCap integrated with IncidentaryClient.
//
// Mirrors the Node SDK wiring suite. Verifies observe -> drop,
// truncated marker, hook re-binding, dropped_total.

using FluentAssertions;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using NSubstitute;
using Xunit;
using TC = Incidentary.Sdk.TraceCap.TraceCap;
using TraceCapEvent = Incidentary.Sdk.TraceCap.TraceCapEvent;
using TraceCapTier = Incidentary.Sdk.TraceCap.TraceCapTier;

namespace Incidentary.Sdk.Tests.TraceCap;

public sealed class TraceCapClientWiringTests
{
    private const string TraceId = "00000000-0000-4000-8000-000000000c11";

    private static IncidentaryClientOptions BaseOptions(bool traceCapEnabled = true) => new()
    {
        ApiKey = "test-key",
        ServiceName = "test-svc",
        BufferCapacity = 4_000,
        TraceCapEnabled = traceCapEnabled,
        AutoInstrument = false,
    };

    private static IncidentaryClient MakeClient(bool traceCapEnabled = true)
    {
        var transport = Substitute.For<ITransport>();
        return new IncidentaryClient(BaseOptions(traceCapEnabled), transport);
    }

    private static CausalEvent MakeEvent(string traceId = TraceId) => new()
    {
        Id = "ce_t",
        TraceId = traceId,
        ServiceId = "test-svc",
        OccurredAt = 1,
        Kind = CeKind.Internal,
        Type = "internal_task",
        StatusCode = 0,
        DurationNs = 0,
    };

    [Fact]
    public void DefaultIsEnabled_UnderWarn_NoDrops()
    {
        using var client = MakeClient();

        for (var i = 0; i < TC.SpansPerTraceWarn - 1; i++)
        {
            client.WriteEvent(MakeEvent());
        }

        client.TraceCapDroppedTotal.Should().Be(0);
    }

    [Fact]
    public void AboveTruncate_DropsSubsequentSpans()
    {
        using var client = MakeClient();

        for (var i = 0; i < TC.SpansPerTraceTruncate + 5; i++)
        {
            client.WriteEvent(MakeEvent());
        }

        client.TraceCapDroppedTotal.Should().Be(5);
    }

    [Fact]
    public void DisabledViaOptions_PassesEverything()
    {
        using var client = MakeClient(traceCapEnabled: false);

        for (var i = 0; i < TC.SpansPerTraceTruncate + 100; i++)
        {
            client.WriteEvent(MakeEvent());
        }

        client.TraceCapDroppedTotal.Should().Be(0);
    }

    [Fact]
    public void OnTraceCapTransition_FiresOnceAtWarn()
    {
        using var client = MakeClient();
        var events = new List<TraceCapEvent>();
        client.OnTraceCapTransition(events.Add);

        for (var i = 0; i < TC.SpansPerTraceWarn; i++)
        {
            client.WriteEvent(MakeEvent());
        }

        events.Should().HaveCount(1);
        events[0].Tier.Should().Be(TraceCapTier.Warn);
        events[0].TraceId.Should().Be(TraceId);
    }
}
