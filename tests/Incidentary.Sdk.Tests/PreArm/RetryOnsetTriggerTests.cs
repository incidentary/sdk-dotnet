namespace Incidentary.Sdk.Tests.PreArm;

using FluentAssertions;
using Incidentary.Sdk.PreArm;
using Xunit;

public sealed class RetryOnsetTriggerTests
{
    private long _now = 100_000;
    private long TimeProvider() => _now;

    private RetryOnsetTrigger CreateTrigger(
        int windowMs = 5000,
        double rateHigh = 0.10,
        double rateMild = 0.05,
        int minTotal = 20,
        int tableSize = 4096) =>
        new(windowMs, rateHigh, rateMild, minTotal, tableSize, TimeProvider);

    [Fact]
    public void BelowMinTotal_ReturnsNone()
    {
        var trigger = CreateTrigger();

        // Only 5 events — below minTotal of 20
        for (var i = 0; i < 5; i++)
            trigger.Record($"key-{i}", isRetry: false);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void HighRetryRate_ReturnsSevere()
    {
        var trigger = CreateTrigger();

        // 30 total calls, 5 retries = ~17% retry rate — above rateHigh of 10%
        for (var i = 0; i < 25; i++)
            trigger.Record($"key-{i}", isRetry: false);
        for (var i = 0; i < 5; i++)
            trigger.Record($"retry-{i}", isRetry: true);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Severe);
        result.TriggerType.Should().Be("retry_onset");
    }

    [Fact]
    public void NoRetries_ReturnsNone()
    {
        var trigger = CreateTrigger();

        // 30 total calls, 0 retries
        for (var i = 0; i < 30; i++)
            trigger.Record($"key-{i}", isRetry: false);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void MildRetryRate_ReturnsMild()
    {
        var trigger = CreateTrigger();

        // 40 total calls, 3 retries = 7.5% — between mild=5% and high=10%
        for (var i = 0; i < 37; i++)
            trigger.Record($"key-{i}", isRetry: false);
        for (var i = 0; i < 3; i++)
            trigger.Record($"retry-{i}", isRetry: true);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Mild);
    }

    [Fact]
    public void OldEventsExpire()
    {
        var trigger = CreateTrigger();

        // Record retries now
        for (var i = 0; i < 20; i++)
            trigger.Record($"key-{i}", isRetry: false);
        for (var i = 0; i < 5; i++)
            trigger.Record($"retry-{i}", isRetry: true);

        trigger.Evaluate().Severity.Should().Be(TriggerSeverity.Severe);

        // Advance time past window (5000ms)
        _now += 6000;

        // Record only non-retries
        for (var i = 0; i < 30; i++)
            trigger.Record($"key-new-{i}", isRetry: false);

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void Constructor_NullTimeProvider_UsesSystemClock()
    {
        // Covers line 41 false branch: `timeProvider ?? (() => Environment.TickCount64)`
        var trigger = new RetryOnsetTrigger(
            windowMs: 5000,
            rateHigh: 0.10,
            rateMild: 0.05,
            minTotal: 20,
            tableSize: 4096,
            timeProvider: null); // ← null → default lambda branch

        trigger.Record("key", isRetry: false);
        var result = trigger.Evaluate();

        result.Should().NotBeNull();
    }
}
