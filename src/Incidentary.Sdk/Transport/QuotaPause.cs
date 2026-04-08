namespace Incidentary.Sdk.Transport;

/// <summary>
/// Pauses event ingestion until the next UTC month when the backend
/// signals that the causal-event quota has been reached (429 + ce_limit_reached).
/// Thread-safe via <see cref="Interlocked.Exchange(ref long, long)"/> on deadline ticks.
/// </summary>
internal sealed class QuotaPause
{
    private long _resumeAtTicks; // DateTimeOffset.UtcTicks

    /// <summary>True if the current UTC time is before the pause deadline.</summary>
    public bool IsPaused => ResumeAt is { } deadline && DateTimeOffset.UtcNow < deadline;

    /// <summary>The point in time when the pause lifts, or null if not paused.</summary>
    public DateTimeOffset? ResumeAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _resumeAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Sets the pause deadline to the first instant of the next UTC month.
    /// </summary>
    public void PauseUntilNextMonth()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMonth = now.Month == 12
            ? new DateTimeOffset(now.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(now.Year, now.Month + 1, 1, 0, 0, 0, TimeSpan.Zero);

        Interlocked.Exchange(ref _resumeAtTicks, nextMonth.UtcTicks);
    }
}
