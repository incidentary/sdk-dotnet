namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Detects concurrent request pileup by comparing current in-flight count
/// against an EWMA baseline, with hold-time confirmation.
/// </summary>
internal sealed class InFlightPileupTrigger
{
    private readonly int _minAbs;
    private readonly double _multiplier;
    private readonly int _netGrowthMin;
    private readonly int _holdMs;
    private readonly int _mildHoldMs;
    private readonly Func<long> _timeProvider;

    private int _inFlight;
    private double _baseline;
    private bool _baselineInitialized;
    private long _conditionMetSince;
    private bool _conditionActive;

    private const double Alpha = 0.05;

    /// <summary>Current number of in-flight requests.</summary>
    public int CurrentInFlight => Math.Max(0, Volatile.Read(ref _inFlight));

    /// <summary>
    /// Creates a new in-flight pileup trigger.
    /// </summary>
    /// <param name="minAbs">Minimum absolute in-flight count to trigger.</param>
    /// <param name="multiplier">Multiplier over baseline to flag pileup.</param>
    /// <param name="netGrowthMin">Minimum net growth above baseline.</param>
    /// <param name="holdSecs">Hold duration (seconds) for Severe.</param>
    /// <param name="mildHoldSecs">Hold duration (seconds) for Mild.</param>
    /// <param name="timeProvider">Monotonic time provider returning milliseconds.</param>
    public InFlightPileupTrigger(
        int minAbs = 32,
        double multiplier = 2.0,
        int netGrowthMin = 16,
        int holdSecs = 3,
        int mildHoldSecs = 2,
        Func<long>? timeProvider = null)
    {
        _minAbs = minAbs;
        _multiplier = multiplier;
        _netGrowthMin = netGrowthMin;
        _holdMs = holdSecs * 1000;
        _mildHoldMs = mildHoldSecs * 1000;
        _timeProvider = timeProvider ?? (() => Environment.TickCount64);
    }

    /// <summary>Increment the in-flight counter when a request starts.</summary>
    public void RecordStart()
    {
        Interlocked.Increment(ref _inFlight);
    }

    /// <summary>Decrement the in-flight counter when a request completes. Updates baseline.</summary>
    public void RecordEnd()
    {
        var current = Interlocked.Decrement(ref _inFlight);

        // Clamp to zero to prevent negative drift.
        // A negative result means RecordEnd was called without a paired RecordStart —
        // skip the baseline update to avoid poisoning the EWMA with a spurious 0 sample.
        if (current < 0)
        {
            Interlocked.CompareExchange(ref _inFlight, 0, current);
            return;
        }

        // Update EWMA baseline based on current in-flight after completion
        var inFlight = Math.Max(0, Volatile.Read(ref _inFlight));
        if (!_baselineInitialized)
        {
            _baseline = inFlight;
            _baselineInitialized = true;
        }
        else
        {
            _baseline = (Alpha * inFlight) + ((1.0 - Alpha) * _baseline);
        }
    }

    /// <summary>
    /// Evaluate the current in-flight pileup conditions.
    /// </summary>
    public TriggerResult Evaluate()
    {
        var now = _timeProvider();
        var inFlight = CurrentInFlight;

        var conditionMet = inFlight >= _minAbs
                           && inFlight >= _baseline * _multiplier
                           && (inFlight - _baseline) >= _netGrowthMin;

        if (!conditionMet)
        {
            _conditionActive = false;
            return TriggerResult.None;
        }

        if (!_conditionActive)
        {
            _conditionActive = true;
            _conditionMetSince = now;
        }

        var holdDuration = now - _conditionMetSince;

        if (holdDuration >= _holdMs)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Severe,
                TriggerType = "in_flight_pileup",
                Reason = $"In-flight {inFlight} (baseline {_baseline:F1}) held for {holdDuration}ms",
            };
        }

        if (holdDuration >= _mildHoldMs)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Mild,
                TriggerType = "in_flight_pileup",
                Reason = $"In-flight {inFlight} (baseline {_baseline:F1}) held for {holdDuration}ms",
            };
        }

        return TriggerResult.None;
    }
}
