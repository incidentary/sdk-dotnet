namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Detects latency anomalies using EWMA baseline comparison on success-like responses.
/// A request is "slow" if its duration exceeds max(minMs, baseline * multiplier).
/// Uses a rolling time-bucketed window (same as ErrorRateTrigger) so counters
/// naturally expire and the trigger reflects recent traffic, not lifetime stats.
/// </summary>
internal sealed class SlowSuccessTrigger
{
    private readonly int _minMs;
    private readonly double _multiplier;
    private readonly double _alpha;
    private readonly double _rateHigh;
    private readonly double _rateMild;
    private readonly int _minSamples;
    private readonly bool _include4xxAsSuccessLike;
    private readonly int _windowBuckets;
    private readonly int _bucketMs;
    private readonly Func<long> _timeProvider;

    private readonly int[] _bucketTotals;
    private readonly int[] _bucketSlows;
    private readonly long[] _bucketStartTimes;

    private double _baseline;
    private bool _baselineInitialized;

    /// <summary>
    /// Creates a new slow success trigger.
    /// </summary>
    /// <param name="minMs">Minimum absolute latency (ms) to consider a request slow.</param>
    /// <param name="multiplier">Multiplier over EWMA baseline to flag as slow.</param>
    /// <param name="alpha">EWMA smoothing factor (0..1).</param>
    /// <param name="rateHigh">Slow-success rate threshold for Severe.</param>
    /// <param name="rateMild">Slow-success rate threshold for Mild.</param>
    /// <param name="minSamples">Minimum samples before evaluation activates.</param>
    /// <param name="include4xxAsSuccessLike">Whether to treat 4xx as success-like.</param>
    /// <param name="windowBuckets">Number of rolling buckets (default 10).</param>
    /// <param name="bucketMs">Duration of each bucket in milliseconds (default 1000).</param>
    /// <param name="timeProvider">Monotonic time provider returning milliseconds.</param>
    public SlowSuccessTrigger(
        int minMs = 250,
        double multiplier = 2.0,
        double alpha = 0.1,
        double rateHigh = 0.20,
        double rateMild = 0.10,
        int minSamples = 50,
        bool include4xxAsSuccessLike = true,
        int windowBuckets = 10,
        int bucketMs = 1000,
        Func<long>? timeProvider = null)
    {
        _minMs = minMs;
        _multiplier = multiplier;
        _alpha = alpha;
        _rateHigh = rateHigh;
        _rateMild = rateMild;
        _minSamples = minSamples;
        _include4xxAsSuccessLike = include4xxAsSuccessLike;
        _windowBuckets = windowBuckets;
        _bucketMs = bucketMs;
        _timeProvider = timeProvider ?? (() => Environment.TickCount64);

        _bucketTotals = new int[windowBuckets];
        _bucketSlows = new int[windowBuckets];
        _bucketStartTimes = new long[windowBuckets];
    }

    /// <summary>
    /// Record a completed request with its status code and duration.
    /// </summary>
    public void Record(int statusCode, long durationMs)
    {
        if (!IsSuccessLike(statusCode))
        {
            return;
        }

        var now = _timeProvider();
        var bucketIndex = GetBucketIndex(now);
        ResetIfStale(bucketIndex, now);

        _bucketTotals[bucketIndex]++;

        // Update EWMA baseline
        if (!_baselineInitialized)
        {
            _baseline = durationMs;
            _baselineInitialized = true;
        }
        else
        {
            _baseline = (_alpha * durationMs) + ((1.0 - _alpha) * _baseline);
        }

        // Check if this request is "slow"
        var slowThreshold = Math.Max(_minMs, _baseline * _multiplier);
        if (durationMs > slowThreshold)
        {
            _bucketSlows[bucketIndex]++;
        }
    }

    /// <summary>
    /// Evaluate the current slow-success rate across all active buckets.
    /// </summary>
    public TriggerResult Evaluate()
    {
        var now = _timeProvider();
        var totalSuccessLike = 0;
        var slowSuccessCount = 0;

        for (var i = 0; i < _windowBuckets; i++)
        {
            if (!IsBucketStale(i, now))
            {
                totalSuccessLike += _bucketTotals[i];
                slowSuccessCount += _bucketSlows[i];
            }
        }

        if (totalSuccessLike < _minSamples)
        {
            return TriggerResult.None;
        }

        var rate = (double)slowSuccessCount / totalSuccessLike;

        if (rate >= _rateHigh)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Severe,
                TriggerType = "slow_success",
                Reason = $"Slow success rate {rate:P1} >= {_rateHigh:P1}",
            };
        }

        if (rate >= _rateMild)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Mild,
                TriggerType = "slow_success",
                Reason = $"Slow success rate {rate:P1} >= {_rateMild:P1}",
            };
        }

        return TriggerResult.None;
    }

    private bool IsSuccessLike(int statusCode)
    {
        if (statusCode >= 200 && statusCode < 300)
        {
            return true;
        }

        return _include4xxAsSuccessLike && statusCode >= 400 && statusCode < 500;
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
            _bucketTotals[index] = 0;
            _bucketSlows[index] = 0;
            _bucketStartTimes[index] = bucketStart;
        }
    }
}
