namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Detects elevated 5xx error rates using a rolling time-bucketed window.
/// </summary>
internal sealed class ErrorRateTrigger
{
    private readonly double _thresholdHigh;
    private readonly double _thresholdLow;
    private readonly int _windowBuckets;
    private readonly int _bucketMs;
    private readonly Func<long> _timeProvider;

    private readonly int[] _totals;
    private readonly int[] _errors;
    private readonly long[] _bucketStartTimes;

    /// <summary>Current error rate percentage (errors / total * 100). Zero when no requests recorded.</summary>
    public double CurrentErrorRate { get; private set; }

    /// <summary>
    /// Creates a new error rate trigger.
    /// </summary>
    /// <param name="thresholdHigh">Error rate percentage to trigger Severe (default 10.0).</param>
    /// <param name="thresholdLow">Error rate percentage to trigger Mild (default 2.0).</param>
    /// <param name="windowBuckets">Number of rolling buckets (default 10).</param>
    /// <param name="bucketMs">Duration of each bucket in milliseconds (default 1000).</param>
    /// <param name="timeProvider">Monotonic time provider returning milliseconds.</param>
    public ErrorRateTrigger(
        double thresholdHigh = 10.0,
        double thresholdLow = 2.0,
        int windowBuckets = 10,
        int bucketMs = 1000,
        Func<long>? timeProvider = null)
    {
        _thresholdHigh = thresholdHigh;
        _thresholdLow = thresholdLow;
        _windowBuckets = windowBuckets;
        _bucketMs = bucketMs;
        _timeProvider = timeProvider ?? (() => Environment.TickCount64);

        _totals = new int[windowBuckets];
        _errors = new int[windowBuckets];
        _bucketStartTimes = new long[windowBuckets];
    }

    /// <summary>
    /// Record a completed request. Status codes in the 5xx range increment the error count.
    /// </summary>
    public void Record(int statusCode)
    {
        var now = _timeProvider();
        var bucketIndex = GetBucketIndex(now);
        ResetIfStale(bucketIndex, now);

        _totals[bucketIndex]++;

        if (statusCode >= 500 && statusCode < 600)
        {
            _errors[bucketIndex]++;
        }
    }

    /// <summary>
    /// Evaluate the current error rate across all active buckets.
    /// </summary>
    public TriggerResult Evaluate()
    {
        var now = _timeProvider();
        var totalRequests = 0;
        var totalErrors = 0;

        for (var i = 0; i < _windowBuckets; i++)
        {
            if (!IsBucketStale(i, now))
            {
                totalRequests += _totals[i];
                totalErrors += _errors[i];
            }
        }

        if (totalRequests == 0)
        {
            CurrentErrorRate = 0;
            return TriggerResult.None;
        }

        CurrentErrorRate = (double)totalErrors / totalRequests * 100.0;

        if (CurrentErrorRate >= _thresholdHigh)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Severe,
                TriggerType = "error_rate",
                Reason = $"5xx error rate {CurrentErrorRate:F1}% >= {_thresholdHigh}%",
            };
        }

        if (CurrentErrorRate >= _thresholdLow)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Mild,
                TriggerType = "error_rate",
                Reason = $"5xx error rate {CurrentErrorRate:F1}% >= {_thresholdLow}%",
            };
        }

        return TriggerResult.None;
    }

    private int GetBucketIndex(long nowMs)
    {
        return (int)((nowMs / _bucketMs) % _windowBuckets);
    }

    private bool IsBucketStale(int index, long nowMs)
    {
        var windowMs = (long)_windowBuckets * _bucketMs;
        return (nowMs - _bucketStartTimes[index]) >= windowMs;
    }

    private void ResetIfStale(int index, long nowMs)
    {
        var bucketStart = (nowMs / _bucketMs) * _bucketMs;

        if (_bucketStartTimes[index] != bucketStart)
        {
            _totals[index] = 0;
            _errors[index] = 0;
            _bucketStartTimes[index] = bucketStart;
        }
    }
}
