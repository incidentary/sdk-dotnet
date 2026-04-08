namespace Incidentary.Sdk.Tests.PreArm;

using FluentAssertions;
using Incidentary.Sdk.PreArm;
using Xunit;

public sealed class SlowSuccessTriggerTests
{
    private long _now = 100_000;
    private long TimeProvider() => _now;

    [Fact]
    public void BelowMinSamples_ReturnsNone()
    {
        var trigger = new SlowSuccessTrigger(
            minMs: 250, multiplier: 2.0, alpha: 0.1,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Only 10 samples — below minSamples of 50
        for (var i = 0; i < 10; i++)
            trigger.Record(200, 100);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void NormalLatency_ReturnsNone()
    {
        var trigger = new SlowSuccessTrigger(
            minMs: 250, multiplier: 2.0, alpha: 0.1,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Record 60 requests all at ~300ms (consistent, normal latency)
        for (var i = 0; i < 60; i++)
            trigger.Record(200, 300);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void HighSlowRate_ReturnsSevere()
    {
        // alpha=0.0 keeps the EWMA baseline fixed so the slow threshold stays constant
        // at max(100, baseline * 2.0), preventing threshold creep during the anomaly window.
        var trigger = new SlowSuccessTrigger(
            minMs: 100, multiplier: 2.0, alpha: 0.0,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Build baseline at 200ms; threshold = max(100, 200*2) = 400ms
        for (var i = 0; i < 50; i++)
            trigger.Record(200, 200);

        // 25 slow at 800ms (all exceed threshold 400ms since alpha=0 keeps it fixed)
        // + 25 normal at 200ms → total 100, 25 slow = 25% rate → Severe (>= 20%)
        for (var i = 0; i < 25; i++)
            trigger.Record(200, 800);
        for (var i = 0; i < 25; i++)
            trigger.Record(200, 200);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Severe);
        result.TriggerType.Should().Be("slow_success");
    }

    [Fact]
    public void MildSlowRate_ReturnsMild()
    {
        // alpha=0.0 keeps the EWMA baseline fixed so the slow threshold stays constant.
        var trigger = new SlowSuccessTrigger(
            minMs: 100, multiplier: 2.0, alpha: 0.0,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Build baseline at 200ms; threshold = 400ms
        for (var i = 0; i < 50; i++)
            trigger.Record(200, 200);

        // 14 slow + 36 normal → 100 total, 14 slow = 14% rate → Mild (10%-20%)
        for (var i = 0; i < 14; i++)
            trigger.Record(200, 800);
        for (var i = 0; i < 36; i++)
            trigger.Record(200, 200);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Mild);
    }

    [Fact]
    public void BelowMinMs_NeverSlow()
    {
        var trigger = new SlowSuccessTrigger(
            minMs: 250, multiplier: 2.0, alpha: 0.1,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Build up baseline at 50ms
        for (var i = 0; i < 60; i++)
            trigger.Record(200, 50);

        // Send requests at 150ms — above 2x baseline (100ms) but below minMs (250ms)
        for (var i = 0; i < 60; i++)
            trigger.Record(200, 150);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void FourXx_CountsAsSuccessLike_WhenEnabled_TriggersMild()
    {
        // alpha=0.0 freezes the baseline so threshold stays deterministic at max(100, 200*2)=400ms
        var trigger = new SlowSuccessTrigger(
            minMs: 100, multiplier: 2.0, alpha: 0.0,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: TimeProvider);

        // Build baseline at 200ms using 4xx responses; threshold = 400ms (frozen, alpha=0)
        for (var i = 0; i < 50; i++)
            trigger.Record(404, 200);

        // 14 slow (800ms > 400ms threshold) + 36 normal (200ms) = 100 total, 14/100 = 14% → Mild
        // This proves 4xx are counted in both the total sample pool AND the slow count.
        for (var i = 0; i < 14; i++)
            trigger.Record(404, 800);
        for (var i = 0; i < 36; i++)
            trigger.Record(404, 200);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Mild);
        result.TriggerType.Should().Be("slow_success");
    }

    [Fact]
    public void FourXx_NotCounted_WhenDisabled()
    {
        // When include4xxAsSuccessLike=false, 4xx should be completely ignored.
        // Even sending many 4xx responses should never meet minSamples.
        var trigger = new SlowSuccessTrigger(
            minMs: 100, multiplier: 2.0, alpha: 0.0,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: false,
            timeProvider: TimeProvider);

        // 100 4xx responses — none should be counted
        for (var i = 0; i < 100; i++)
            trigger.Record(404, 800);

        // minSamples never met (all ignored) → None
        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void StaleBuckets_AreExcludedFromEvaluation()
    {
        // alpha=0.0 so threshold stays fixed; makes slow classification deterministic
        var trigger = new SlowSuccessTrigger(
            minMs: 100, multiplier: 2.0, alpha: 0.0,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            windowBuckets: 10, bucketMs: 1000,
            timeProvider: TimeProvider);

        // Build baseline at 200ms; threshold = 400ms
        for (var i = 0; i < 50; i++)
            trigger.Record(200, 200);

        // 25 slow at 800ms → 25/75 = 33% rate → Severe
        for (var i = 0; i < 25; i++)
            trigger.Record(200, 800);

        // Verify it triggers now
        trigger.Evaluate().Severity.Should().Be(TriggerSeverity.Severe);

        // Advance time beyond the window (10 buckets * 1000ms = 10s)
        _now += 11_000;

        // Old buckets are stale, no data in new window → None
        trigger.Evaluate().Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void Constructor_NullTimeProvider_UsesSystemClock()
    {
        // Covers line 63 false branch: `timeProvider ?? (() => Environment.TickCount64)`
        var trigger = new SlowSuccessTrigger(
            minMs: 250, multiplier: 2.0, alpha: 0.1,
            rateHigh: 0.20, rateMild: 0.10, minSamples: 50,
            include4xxAsSuccessLike: true,
            timeProvider: null); // ← null → default lambda branch

        trigger.Record(200, 100);
        var result = trigger.Evaluate();

        result.Should().NotBeNull();
    }
}
