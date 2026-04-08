namespace Incidentary.Sdk.Tests.PreArm;

using FluentAssertions;
using Incidentary.Sdk.PreArm;
using Xunit;

public sealed class ErrorRateTriggerTests
{
    private long _now = 100_000;
    private long TimeProvider() => _now;

    private ErrorRateTrigger CreateTrigger(
        double thresholdHigh = 10.0,
        double thresholdLow = 2.0,
        int windowBuckets = 10,
        int bucketMs = 1000) =>
        new(thresholdHigh, thresholdLow, windowBuckets, bucketMs, TimeProvider);

    [Fact]
    public void NoRequests_ReturnsNone()
    {
        var trigger = CreateTrigger();

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void BelowThreshold_ReturnsNone()
    {
        var trigger = CreateTrigger();

        // Record 100 requests: 1 error (1% rate) — below low threshold of 2%
        for (var i = 0; i < 99; i++)
            trigger.Record(200);
        trigger.Record(500);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
        trigger.CurrentErrorRate.Should().BeApproximately(1.0, 0.1);
    }

    [Fact]
    public void AboveLowBelowHigh_ReturnsMild()
    {
        var trigger = CreateTrigger();

        // Record 100 requests: 5 errors (5% rate) — between low=2% and high=10%
        for (var i = 0; i < 95; i++)
            trigger.Record(200);
        for (var i = 0; i < 5; i++)
            trigger.Record(503);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Mild);
        trigger.CurrentErrorRate.Should().BeApproximately(5.0, 0.1);
    }

    [Fact]
    public void AboveHighThreshold_ReturnsSevere()
    {
        var trigger = CreateTrigger();

        // Record 100 requests: 15 errors (15% rate) — above high threshold of 10%
        for (var i = 0; i < 85; i++)
            trigger.Record(200);
        for (var i = 0; i < 15; i++)
            trigger.Record(500);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Severe);
        result.TriggerType.Should().Be("error_rate");
        trigger.CurrentErrorRate.Should().BeApproximately(15.0, 0.1);
    }

    [Fact]
    public void OldBucketsExpire()
    {
        var trigger = CreateTrigger();

        // Record errors in current time
        for (var i = 0; i < 10; i++)
            trigger.Record(500);

        trigger.Evaluate().Severity.Should().Be(TriggerSeverity.Severe);

        // Advance time past the full window (10 buckets * 1000ms = 10,000ms)
        _now += 11_000;

        // Record only successes in the new window
        for (var i = 0; i < 20; i++)
            trigger.Record(200);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
        trigger.CurrentErrorRate.Should().BeApproximately(0.0, 0.1);
    }

    [Fact]
    public void OnlyFiveXx_CountAsErrors()
    {
        var trigger = CreateTrigger();

        // 4xx should NOT count as errors
        for (var i = 0; i < 50; i++)
            trigger.Record(200);
        for (var i = 0; i < 50; i++)
            trigger.Record(400);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
        trigger.CurrentErrorRate.Should().BeApproximately(0.0, 0.1);
    }

    [Fact]
    public void Constructor_NullTimeProvider_UsesSystemClock()
    {
        // Covers line 40 false branch: `timeProvider ?? (() => Environment.TickCount64)`
        // When timeProvider is null, the constructor uses the default lambda.
        var trigger = new ErrorRateTrigger(
            thresholdHigh: 10.0,
            thresholdLow: 2.0,
            windowBuckets: 10,
            bucketMs: 1000,
            timeProvider: null); // ← null → default lambda branch

        trigger.Record(200);
        var result = trigger.Evaluate();

        // Should not throw; just verifies the trigger works with system clock
        result.Should().NotBeNull();
    }
}
