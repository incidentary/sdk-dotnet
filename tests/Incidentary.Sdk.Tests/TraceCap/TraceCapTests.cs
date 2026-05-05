// L1 — SDK-side trace cap (cross-SDK spec at docs/specs/l1-trace-cap.md).
// Mirror of the Node, Python, Go acceptance suites.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Incidentary.Sdk.TraceCap;
using Xunit;

namespace Incidentary.Sdk.Tests.TraceCap;

public class TraceCapTests
{
    private const string TidA = "00000000-0000-4000-8000-0000000000a1";
    private const string TidB = "00000000-0000-4000-8000-0000000000b2";

    private static (Sdk.TraceCap.TraceCap cap, List<TraceCapEvent> events) MakeCap(
        bool enabled = true,
        int maxTrackedTraces = 0,
        int maxBlacklistedTraces = 0,
        string serviceId = "test-svc")
    {
        var events = new List<TraceCapEvent>();
        var cap = new Sdk.TraceCap.TraceCap(new TraceCapOptions
        {
            ServiceId = serviceId,
            Enabled = enabled,
            MaxTrackedTraces = maxTrackedTraces,
            MaxBlacklistedTraces = maxBlacklistedTraces,
            Hook = events.Add,
        });
        return (cap, events);
    }

    private static (int accepted, int dropped) EmitN(Sdk.TraceCap.TraceCap cap, string traceId, int n)
    {
        int accepted = 0, dropped = 0;
        for (int i = 0; i < n; i++)
        {
            if (cap.Observe(traceId).ShouldDrop) dropped++;
            else accepted++;
        }
        return (accepted, dropped);
    }

    [Fact]
    public void Constants_match_spec()
    {
        Sdk.TraceCap.TraceCap.SpansPerTraceWarn.Should().Be(5_000);
        Sdk.TraceCap.TraceCap.SpansPerTraceTruncate.Should().Be(50_000);
        Sdk.TraceCap.TraceCap.SpansPerTraceBreaker.Should().Be(500_000);
    }

    [Fact]
    public void Under_warn_threshold_passes_all_spans()
    {
        var (cap, events) = MakeCap();
        var (accepted, dropped) = EmitN(cap, TidA, (int)(Sdk.TraceCap.TraceCap.SpansPerTraceWarn - 1));
        accepted.Should().Be((int)(Sdk.TraceCap.TraceCap.SpansPerTraceWarn - 1));
        dropped.Should().Be(0);
        events.Should().BeEmpty();
    }

    [Fact]
    public void At_warn_threshold_fires_once()
    {
        var (cap, events) = MakeCap();
        var (accepted, dropped) = EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        accepted.Should().Be((int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        dropped.Should().Be(0);
        events.Should().HaveCount(1);
        var ev = events[0];
        ev.Tier.Should().Be(TraceCapTier.Warn);
        ev.TraceId.Should().Be(TidA);
        ev.CumulativeSpanCount.Should().Be(Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        ev.ServiceId.Should().Be("test-svc");
        ev.TimestampMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Crossing_warn_in_one_span_fires_once_only()
    {
        var (cap, events) = MakeCap();
        EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        EmitN(cap, TidA, 1_000);
        events.FindAll(e => e.Tier == TraceCapTier.Warn).Should().HaveCount(1);
    }

    [Fact]
    public void At_truncate_threshold_drops_subsequent()
    {
        var (cap, events) = MakeCap();
        var (a1, d1) = EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceTruncate);
        a1.Should().Be((int)Sdk.TraceCap.TraceCap.SpansPerTraceTruncate);
        d1.Should().Be(0);
        var (a2, d2) = EmitN(cap, TidA, 1_000);
        a2.Should().Be(0);
        d2.Should().Be(1_000);
        events.FindAll(e => e.Tier == TraceCapTier.Truncate).Should().HaveCount(1);
    }

    [Fact]
    public void At_breaker_threshold_drops_subsequent()
    {
        var (cap, events) = MakeCap();
        EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceBreaker);
        events.FindAll(e => e.Tier == TraceCapTier.Warn).Should().HaveCount(1);
        events.FindAll(e => e.Tier == TraceCapTier.Truncate).Should().HaveCount(1);
        events.FindAll(e => e.Tier == TraceCapTier.Breaker).Should().HaveCount(1);
        var (_, dropped) = EmitN(cap, TidA, 1);
        dropped.Should().Be(1);
        events.Should().HaveCount(3);
    }

    [Fact]
    public void Distinct_trace_ids_isolated()
    {
        var (cap, events) = MakeCap();
        EmitN(cap, TidA, (int)(Sdk.TraceCap.TraceCap.SpansPerTraceWarn - 1));
        EmitN(cap, TidB, (int)(Sdk.TraceCap.TraceCap.SpansPerTraceWarn - 1));
        events.Should().BeEmpty();
    }

    [Fact]
    public void Lru_evicts_oldest_under_pressure()
    {
        var (cap, events) = MakeCap(maxTrackedTraces: 8);
        cap.Observe(TidA);
        for (int i = 0; i < 16; i++) cap.Observe($"evict-{i}-{TidB}");
        EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        events.FindAll(e => e.Tier == TraceCapTier.Warn).Should().HaveCount(1);
    }

    [Fact]
    public void Breaker_blacklist_persists_across_evictions()
    {
        var (cap, _) = MakeCap(maxTrackedTraces: 8);
        EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceBreaker);
        for (int i = 0; i < 16; i++) cap.Observe($"flood-{i}");
        var verdict = cap.Observe(TidA);
        verdict.ShouldDrop.Should().BeTrue();
        verdict.Reason.Should().Be(VerdictReason.Breaker);
    }

    [Fact]
    public void Opt_out_disables_all_caps()
    {
        var (cap, events) = MakeCap(enabled: false);
        var (accepted, dropped) = EmitN(cap, TidA, 600_000);
        accepted.Should().Be(600_000);
        dropped.Should().Be(0);
        events.Should().BeEmpty();
    }

    [Fact]
    public void Hook_receives_correct_payload()
    {
        var received = new List<TraceCapEvent>();
        var cap = new Sdk.TraceCap.TraceCap(new TraceCapOptions
        {
            ServiceId = "svc-payments",
            Enabled = true,
            Hook = received.Add,
        });
        EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        received.Should().HaveCount(1);
        var ev = received[0];
        ev.Tier.Should().Be(TraceCapTier.Warn);
        ev.TraceId.Should().Be(TidA);
        ev.CumulativeSpanCount.Should().Be(Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        ev.ServiceId.Should().Be("svc-payments");
    }

    [Fact]
    public void Empty_trace_id_accepted()
    {
        var (cap, _) = MakeCap();
        cap.Observe(string.Empty).ShouldDrop.Should().BeFalse();
        cap.Observe(null).ShouldDrop.Should().BeFalse();
    }

    [Fact]
    public void Hook_errors_swallowed()
    {
        var cap = new Sdk.TraceCap.TraceCap(new TraceCapOptions
        {
            ServiceId = "svc",
            Enabled = true,
            Hook = _ => throw new InvalidOperationException("boom"),
        });
        var act = () => EmitN(cap, TidA, (int)Sdk.TraceCap.TraceCap.SpansPerTraceWarn);
        act.Should().NotThrow();
    }

    [Fact]
    public void Blacklist_itself_is_bounded()
    {
        var (cap, _) = MakeCap(maxBlacklistedTraces: 4);
        for (int i = 0; i < 6; i++)
            EmitN(cap, $"breaker-{i}", (int)Sdk.TraceCap.TraceCap.SpansPerTraceBreaker);
        cap.Observe("breaker-0").ShouldDrop.Should().BeFalse();
    }
}
