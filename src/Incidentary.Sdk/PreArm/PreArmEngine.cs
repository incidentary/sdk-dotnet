namespace Incidentary.Sdk.PreArm;

/// <summary>
/// State machine managing the pre-arm lifecycle: NORMAL -> PRE_ARMED -> INCIDENT.
/// Coordinates all trigger evaluations and mode transitions.
/// </summary>
internal sealed class PreArmEngine
{
    private readonly IncidentaryClientOptions _options;
    private readonly Func<long> _timeProvider;
    private readonly Action<ClientCaptureMode>? _onModeChanged;

    private readonly ErrorRateTrigger _errorRateTrigger;
    private readonly SlowSuccessTrigger _slowSuccessTrigger;
    private readonly InFlightPileupTrigger _inFlightPileupTrigger;
    private readonly RetryOnsetTrigger _retryOnsetTrigger;

    private readonly object _lock = new();

    private ClientCaptureMode _mode = ClientCaptureMode.Normal;
    private long _preArmEnteredAt;
    private long _preArmExitedAt;
    private string? _boundIncidentId;

    // Counters for debug state
    private long _preArmEnterTotal;
    private long _preArmExitTotal;
    private long _escalateTotal;

    // Active window tracking
    private List<string> _activeTriggerReasons = [];

    // Recent windows (up to 5)
    private readonly Queue<PreArmWindowInfo> _recentWindows = new();

    /// <summary>Current capture mode.</summary>
    public ClientCaptureMode Mode
    {
        get
        {
            lock (_lock)
            {
                return _mode;
            }
        }
    }

    /// <summary>
    /// Creates a new pre-arm engine.
    /// </summary>
    /// <param name="options">Client configuration options with pre-arm thresholds.</param>
    /// <param name="timeProvider">Monotonic time provider returning milliseconds.</param>
    /// <param name="onModeChanged">Callback invoked when the capture mode changes.</param>
    public PreArmEngine(
        IncidentaryClientOptions options,
        Func<long>? timeProvider = null,
        Action<ClientCaptureMode>? onModeChanged = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? (() => Environment.TickCount64);
        _onModeChanged = onModeChanged;

        _errorRateTrigger = new ErrorRateTrigger(
            options.PreArmThresholdHigh,
            options.PreArmThresholdLow,
            windowBuckets: 10,
            bucketMs: 1000,
            _timeProvider);

        _slowSuccessTrigger = new SlowSuccessTrigger(
            options.PreArmSlowMinMs,
            options.PreArmSlowMultiplier,
            options.PreArmSlowAlpha,
            options.PreArmSlowSuccessRateHigh,
            options.PreArmSlowSuccessRateMild,
            options.PreArmSlowMinSamples,
            options.PreArmSlowInclude4xxAsSuccessLike,
            timeProvider: _timeProvider);

        _inFlightPileupTrigger = new InFlightPileupTrigger(
            options.PreArmInFlightMinAbs,
            options.PreArmInFlightMultiplier,
            options.PreArmInFlightNetGrowthMin,
            options.PreArmInFlightHoldSecs,
            options.PreArmInFlightMildHoldSecs,
            _timeProvider);

        _retryOnsetTrigger = new RetryOnsetTrigger(
            options.PreArmRetryWindowMs,
            options.PreArmRetryRateHigh,
            options.PreArmRetryRateMild,
            options.PreArmRetryMinTotal,
            options.PreArmRetryTableSize,
            _timeProvider);
    }

    /// <summary>
    /// Called when an inbound/outbound request completes.
    /// Records data for error rate, slow success, and retry triggers, then evaluates state transitions.
    /// </summary>
    public void OnRequestCompleted(int statusCode, long durationMs, string? edgeKeyHash, bool isRetry)
    {
        lock (_lock)
        {
            _errorRateTrigger.Record(statusCode);
            _slowSuccessTrigger.Record(statusCode, durationMs);
            _retryOnsetTrigger.Record(edgeKeyHash, isRetry);
        }

        EvaluateAndTransition();
    }

    /// <summary>Called when a request starts (for in-flight tracking).</summary>
    public void OnRequestStarted()
    {
        lock (_lock)
        {
            _inFlightPileupTrigger.RecordStart();
        }
    }

    /// <summary>Called when a request ends (for in-flight tracking).</summary>
    public void OnRequestEnded()
    {
        lock (_lock)
        {
            _inFlightPileupTrigger.RecordEnd();
        }
    }

    /// <summary>
    /// Escalate to INCIDENT mode. Can be called from any state.
    /// </summary>
    public void EscalateToIncident(string? incidentId)
    {
        ClientCaptureMode? notifyMode;
        lock (_lock)
        {
            _boundIncidentId = incidentId;
            _escalateTotal++;

            if (_mode == ClientCaptureMode.Normal)
            {
                // Enter pre-arm first for bookkeeping, then immediately escalate
                _preArmEnteredAt = _timeProvider();
                _preArmEnterTotal++;
                _activeTriggerReasons = ["external_escalation"];
            }

            notifyMode = TransitionTo(ClientCaptureMode.Incident);
        }
        if (notifyMode.HasValue)
            _onModeChanged?.Invoke(notifyMode.Value);
    }

    /// <summary>
    /// Close the active incident. Returns to NORMAL mode.
    /// </summary>
    public void CloseIncident()
    {
        ClientCaptureMode? notifyMode;
        lock (_lock)
        {
            if (_mode != ClientCaptureMode.Incident)
                return;

            RecordWindowClose("incident_closed");
            _boundIncidentId = null;
            notifyMode = TransitionTo(ClientCaptureMode.Normal);
        }
        if (notifyMode.HasValue)
            _onModeChanged?.Invoke(notifyMode.Value);
    }

    /// <summary>
    /// Returns a snapshot of the engine state for observability.
    /// </summary>
    public PreArmDebugState GetDebugState()
    {
        lock (_lock)
        {
            var counters = new Dictionary<string, long>
            {
                ["prearm_enter_total"] = _preArmEnterTotal,
                ["prearm_exit_total"] = _preArmExitTotal,
                ["escalate_total"] = _escalateTotal,
            };

            PreArmWindowInfo? activeWindow = null;
            if (_mode == ClientCaptureMode.PreArmed || _mode == ClientCaptureMode.Incident)
            {
                activeWindow = new PreArmWindowInfo
                {
                    StartedAtTicks = _preArmEnteredAt,
                    TriggerReasons = _activeTriggerReasons.AsReadOnly(),
                    BoundIncidentId = _boundIncidentId,
                };
            }

            return new PreArmDebugState
            {
                Mode = _mode,
                Counters = counters,
                ActiveWindow = activeWindow,
                RecentWindows = _recentWindows.ToArray(),
                ActiveTriggers = _mode != ClientCaptureMode.Normal
                    ? _activeTriggerReasons.AsReadOnly()
                    : null,
            };
        }
    }

    private void EvaluateAndTransition()
    {
        ClientCaptureMode? notifyMode = null;

        lock (_lock)
        {
            var errorResult = _errorRateTrigger.Evaluate();
            var slowResult = _options.PreArmEnableSlowSuccess
                ? _slowSuccessTrigger.Evaluate()
                : TriggerResult.None;
            var inFlightResult = _options.PreArmEnableInFlight
                ? _inFlightPileupTrigger.Evaluate()
                : TriggerResult.None;
            var retryResult = _options.PreArmEnableRetry
                ? _retryOnsetTrigger.Evaluate()
                : TriggerResult.None;

            var combined = TriggerArbiter.Evaluate(errorResult, slowResult, inFlightResult, retryResult);
            var now = _timeProvider();

            switch (_mode)
            {
                case ClientCaptureMode.Normal:
                    if (combined.Severity >= TriggerSeverity.Severe && !IsInCooldown(now))
                    {
                        _preArmEnteredAt = now;
                        _preArmEnterTotal++;
                        _activeTriggerReasons = BuildTriggerReasons(combined);
                        notifyMode = TransitionTo(ClientCaptureMode.PreArmed);
                    }

                    break;

                case ClientCaptureMode.PreArmed:
                    // Check TTL expiration
                    if (now - _preArmEnteredAt >= _options.PreArmTtlMs)
                    {
                        RecordWindowClose("ttl_expired");
                        _preArmExitedAt = now;
                        _preArmExitTotal++;
                        notifyMode = TransitionTo(ClientCaptureMode.Normal);
                        break;
                    }

                    // Update active trigger reasons
                    if (combined.Severity >= TriggerSeverity.Mild)
                    {
                        _activeTriggerReasons = BuildTriggerReasons(combined);
                    }

                    // Check if triggers have cleared and min duration has elapsed
                    if (combined.Severity == TriggerSeverity.None
                        && (now - _preArmEnteredAt) >= _options.PreArmMinDurationMs)
                    {
                        RecordWindowClose("triggers_cleared");
                        _preArmExitedAt = now;
                        _preArmExitTotal++;
                        notifyMode = TransitionTo(ClientCaptureMode.Normal);
                    }

                    break;

                case ClientCaptureMode.Incident:
                    // Incident mode is only exited via CloseIncident()
                    break;
            }
        }

        // Invoke the mode-changed callback outside the lock to prevent deadlocks
        // if the callback re-enters the client (e.g., logging or escalation).
        if (notifyMode.HasValue)
            _onModeChanged?.Invoke(notifyMode.Value);
    }

    private bool IsInCooldown(long now)
    {
        return _preArmExitedAt > 0 && (now - _preArmExitedAt) < _options.PreArmCooldownMs;
    }

    /// <summary>
    /// Sets the mode and returns the new mode if a transition actually occurred,
    /// or null if the mode was already set to that value.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private ClientCaptureMode? TransitionTo(ClientCaptureMode newMode)
    {
        if (_mode == newMode)
            return null;

        _mode = newMode;
        return newMode;
    }

    private void RecordWindowClose(string reason)
    {
        // Must be called under _lock
        var window = new PreArmWindowInfo
        {
            StartedAtTicks = _preArmEnteredAt,
            EndedAtTicks = _timeProvider(),
            TriggerReasons = _activeTriggerReasons.AsReadOnly(),
            CloseReason = reason,
            BoundIncidentId = _boundIncidentId,
        };

        _recentWindows.Enqueue(window);

        // Keep only the last 5
        while (_recentWindows.Count > 5)
        {
            _recentWindows.Dequeue();
        }
    }

    private static List<string> BuildTriggerReasons(TriggerResult combined)
    {
        var reasons = new List<string>();
        if (combined.TriggerType is not null)
        {
            reasons.AddRange(combined.TriggerType.Split('+'));
        }

        return reasons;
    }
}
