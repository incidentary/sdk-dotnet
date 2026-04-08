namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Detects sudden bursts of retries in a sliding time window.
/// </summary>
internal sealed class RetryOnsetTrigger
{
    private readonly int _windowMs;
    private readonly double _rateHigh;
    private readonly double _rateMild;
    private readonly int _minTotal;
    private readonly Func<long> _timeProvider;

    private readonly int _bucketCount;
    private readonly int _bucketMs;
    private readonly int[] _totals;
    private readonly int[] _retries;
    private readonly long[] _bucketStartTimes;

    /// <summary>
    /// Creates a new retry onset trigger.
    /// </summary>
    /// <param name="windowMs">Sliding window duration in milliseconds (default 5000).</param>
    /// <param name="rateHigh">Retry rate threshold for Severe (default 0.10).</param>
    /// <param name="rateMild">Retry rate threshold for Mild (default 0.05).</param>
    /// <param name="minTotal">Minimum total events before evaluation activates (default 20).</param>
    /// <param name="tableSize">Hash table size (unused, reserved for future dedup).</param>
    /// <param name="timeProvider">Monotonic time provider returning milliseconds.</param>
    public RetryOnsetTrigger(
        int windowMs = 5000,
        double rateHigh = 0.10,
        double rateMild = 0.05,
        int minTotal = 20,
        int tableSize = 4096,
        Func<long>? timeProvider = null)
    {
        _windowMs = windowMs;
        _rateHigh = rateHigh;
        _rateMild = rateMild;
        _minTotal = minTotal;
        _timeProvider = timeProvider ?? (() => Environment.TickCount64);

        // Use 10 buckets within the window
        _bucketCount = 10;
        _bucketMs = Math.Max(1, windowMs / _bucketCount);
        _totals = new int[_bucketCount];
        _retries = new int[_bucketCount];
        _bucketStartTimes = new long[_bucketCount];
    }

    /// <summary>
    /// Record an outbound call.
    /// </summary>
    /// <param name="edgeKeyHash">Optional edge key hash (reserved for dedup).</param>
    /// <param name="isRetry">Whether this call is a retry.</param>
    public void Record(string? edgeKeyHash, bool isRetry)
    {
        var now = _timeProvider();
        var bucketIndex = GetBucketIndex(now);
        ResetIfStale(bucketIndex, now);

        _totals[bucketIndex]++;

        if (isRetry)
        {
            _retries[bucketIndex]++;
        }
    }

    /// <summary>
    /// Evaluate the current retry rate across all active buckets.
    /// </summary>
    public TriggerResult Evaluate()
    {
        var now = _timeProvider();
        var totalCount = 0;
        var retryCount = 0;

        for (var i = 0; i < _bucketCount; i++)
        {
            if (!IsBucketStale(i, now))
            {
                totalCount += _totals[i];
                retryCount += _retries[i];
            }
        }

        if (totalCount < _minTotal)
        {
            return TriggerResult.None;
        }

        var rate = (double)retryCount / totalCount;

        if (rate >= _rateHigh)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Severe,
                TriggerType = "retry_onset",
                Reason = $"Retry rate {rate:P1} >= {_rateHigh:P1}",
            };
        }

        if (rate >= _rateMild)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Mild,
                TriggerType = "retry_onset",
                Reason = $"Retry rate {rate:P1} >= {_rateMild:P1}",
            };
        }

        return TriggerResult.None;
    }

    private int GetBucketIndex(long nowMs)
    {
        return (int)((nowMs / _bucketMs) % _bucketCount);
    }

    private bool IsBucketStale(int index, long nowMs)
    {
        var windowMs = (long)_bucketCount * _bucketMs;
        return (nowMs - _bucketStartTimes[index]) >= windowMs;
    }

    private void ResetIfStale(int index, long nowMs)
    {
        var bucketStart = (nowMs / _bucketMs) * _bucketMs;

        if (_bucketStartTimes[index] != bucketStart)
        {
            _totals[index] = 0;
            _retries[index] = 0;
            _bucketStartTimes[index] = bucketStart;
        }
    }
}
