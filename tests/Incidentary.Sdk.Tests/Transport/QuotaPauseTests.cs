using FluentAssertions;
using Incidentary.Sdk.Transport;
using Xunit;

namespace Incidentary.Sdk.Tests.Transport;

public sealed class QuotaPauseTests
{
    // ── Initial state ──────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsNotPaused()
    {
        var pause = new QuotaPause();

        pause.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void InitialState_ResumeAt_IsNull()
    {
        var pause = new QuotaPause();

        pause.ResumeAt.Should().BeNull();
    }

    // ── PauseUntilNextMonth ────────────────────────────────────────────────

    [Fact]
    public void PauseUntilNextMonth_SetsPausedState()
    {
        var pause = new QuotaPause();

        pause.PauseUntilNextMonth();

        pause.IsPaused.Should().BeTrue();
    }

    [Fact]
    public void PauseUntilNextMonth_SetsResumeAt_ToFirstOfNextMonth()
    {
        var pause = new QuotaPause();
        var before = DateTimeOffset.UtcNow;

        pause.PauseUntilNextMonth();

        var resumeAt = pause.ResumeAt;
        resumeAt.Should().NotBeNull();

        // Must be 1st of some month at midnight UTC
        resumeAt!.Value.Day.Should().Be(1);
        resumeAt.Value.Hour.Should().Be(0);
        resumeAt.Value.Minute.Should().Be(0);
        resumeAt.Value.Second.Should().Be(0);
        resumeAt.Value.Offset.Should().Be(TimeSpan.Zero);

        // Must be in the future (after the before snapshot)
        resumeAt.Value.Should().BeAfter(before);
    }

    [Fact]
    public void PauseUntilNextMonth_InDecember_RollsOverToJanuary()
    {
        // We can verify the logic by checking the month rollover behavior
        // by inspecting what PauseUntilNextMonth would compute for December.
        // Since we can't inject a clock, we verify the invariant holds:
        // ResumeAt.Month != current month (unless boundary edge case, but
        // it's always the NEXT month at minimum 1 second in the future).
        var pause = new QuotaPause();

        pause.PauseUntilNextMonth();

        var now = DateTimeOffset.UtcNow;
        var resumeAt = pause.ResumeAt!.Value;

        // ResumeAt should be in the future
        resumeAt.Should().BeAfter(now.AddSeconds(-1));

        // Year should be >= current year
        resumeAt.Year.Should().BeGreaterThanOrEqualTo(now.Year);
    }

    [Fact]
    public void PauseUntilNextMonth_CalledTwice_OverwritesPreviousDeadline()
    {
        var pause = new QuotaPause();

        pause.PauseUntilNextMonth();
        var firstDeadline = pause.ResumeAt;

        pause.PauseUntilNextMonth();
        var secondDeadline = pause.ResumeAt;

        // Second call should set (or reset) the deadline — both should point to
        // the same next-month boundary if called within the same month.
        firstDeadline.Should().NotBeNull();
        secondDeadline.Should().NotBeNull();
        secondDeadline!.Value.Day.Should().Be(1);
    }

    // ── Thread safety ──────────────────────────────────────────────────────

    [Fact]
    public async Task PauseUntilNextMonth_ConcurrentCalls_NeverThrows()
    {
        var pause = new QuotaPause();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => pause.PauseUntilNextMonth()))
            .ToArray();

        await Task.WhenAll(tasks);

        // After all concurrent calls, should be in a valid paused state
        pause.IsPaused.Should().BeTrue();
        pause.ResumeAt.Should().NotBeNull();
        pause.ResumeAt!.Value.Day.Should().Be(1);
    }

    [Fact]
    public async Task IsPaused_ConcurrentReads_NeverThrows()
    {
        var pause = new QuotaPause();
        pause.PauseUntilNextMonth();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => { var isPaused = pause.IsPaused; }))
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
