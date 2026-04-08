namespace Incidentary.Sdk.Tests.PreArm;

using FluentAssertions;
using Incidentary.Sdk.PreArm;
using Xunit;

public sealed class InFlightPileupTriggerTests
{
    private long _now = 100_000;
    private long TimeProvider() => _now;

    private InFlightPileupTrigger CreateTrigger(
        int minAbs = 32,
        double multiplier = 2.0,
        int netGrowthMin = 16,
        int holdSecs = 3,
        int mildHoldSecs = 2) =>
        new(minAbs, multiplier, netGrowthMin, holdSecs, mildHoldSecs, TimeProvider);

    [Fact]
    public void BelowMinAbs_ReturnsNone()
    {
        var trigger = CreateTrigger();

        // Only 10 in flight — below minAbs of 32
        for (var i = 0; i < 10; i++)
            trigger.RecordStart();

        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.None);
        trigger.CurrentInFlight.Should().Be(10);
    }

    [Fact]
    public void HighPileup_ReturnsSevere()
    {
        var trigger = CreateTrigger();

        // Build a low baseline: start and complete many requests
        for (var cycle = 0; cycle < 100; cycle++)
        {
            trigger.RecordStart();
            trigger.RecordEnd();
            _now += 10;
        }

        // Now pile up 50 requests without completing them
        for (var i = 0; i < 50; i++)
            trigger.RecordStart();

        // First evaluate: records conditionMetSince = _now
        trigger.Evaluate();

        // Advance time past holdSecs (3s = 3000ms) so hold condition is satisfied
        _now += 4000;

        // Second evaluate: holdDuration >= holdMs → Severe
        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Severe);
        result.TriggerType.Should().Be("in_flight_pileup");
        trigger.CurrentInFlight.Should().Be(50);
    }

    [Fact]
    public void RecordStartEnd_TracksCorrectly()
    {
        var trigger = CreateTrigger();

        // Start 5, end 3 → inFlight should be 2
        for (var i = 0; i < 5; i++)
            trigger.RecordStart();
        for (var i = 0; i < 3; i++)
            trigger.RecordEnd();

        trigger.CurrentInFlight.Should().Be(2);
    }

    [Fact]
    public void InFlight_NeverGoesNegative()
    {
        var trigger = CreateTrigger();

        // End without start
        trigger.RecordEnd();

        trigger.CurrentInFlight.Should().Be(0);
    }

    [Fact]
    public void HighPileup_MildHoldDuration_ReturnsMild()
    {
        // holdSecs=3 (3000ms Severe), mildHoldSecs=2 (2000ms Mild)
        var trigger = CreateTrigger(holdSecs: 3, mildHoldSecs: 2);

        // Build a near-zero baseline
        for (var cycle = 0; cycle < 100; cycle++)
        {
            trigger.RecordStart();
            trigger.RecordEnd();
            _now += 10;
        }

        // Pile up 50 requests without completing
        for (var i = 0; i < 50; i++)
            trigger.RecordStart();

        // First evaluate: records conditionMetSince = _now
        trigger.Evaluate();

        // Advance time into the mild window: >= mildHoldMs (2000) but < holdMs (3000)
        _now += 2500;

        // Second evaluate: 2000ms <= holdDuration < 3000ms → Mild
        var result = trigger.Evaluate();

        result.Severity.Should().Be(TriggerSeverity.Mild);
        result.TriggerType.Should().Be("in_flight_pileup");
    }

    [Fact]
    public void HighPileup_BelowMildThreshold_ReturnsNone()
    {
        var trigger = CreateTrigger(holdSecs: 3, mildHoldSecs: 2);

        for (var cycle = 0; cycle < 100; cycle++)
        {
            trigger.RecordStart();
            trigger.RecordEnd();
            _now += 10;
        }

        for (var i = 0; i < 50; i++)
            trigger.RecordStart();

        // First evaluate — starts the hold timer
        trigger.Evaluate();

        // Advance time but stay below mildHoldMs (< 2000ms)
        _now += 1000;

        var result = trigger.Evaluate();

        // Hold duration < mildHoldMs → still None
        result.Severity.Should().Be(TriggerSeverity.None);
    }

    [Fact]
    public void Constructor_NullTimeProvider_UsesSystemClock()
    {
        // Covers line 49 false branch: `timeProvider ?? (() => Environment.TickCount64)`
        var trigger = new InFlightPileupTrigger(
            minAbs: 32,
            multiplier: 2.0,
            netGrowthMin: 16,
            holdSecs: 3,
            mildHoldSecs: 2,
            timeProvider: null); // ← null → default lambda branch

        trigger.RecordStart();
        var result = trigger.Evaluate();

        result.Should().NotBeNull();
    }
}
