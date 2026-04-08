namespace Incidentary.Sdk.Transport;

/// <summary>
/// Thread-safe circuit breaker that opens after consecutive failures
/// and allows a single probe request after a cooldown period.
/// </summary>
internal sealed class CircuitBreaker
{
    private readonly int _maxFailures;
    private readonly long _cooldownMs;
    private readonly Func<long> _tickProvider;

    private long _consecutiveFailures;
    private long _openedAtTick;

    /// <param name="maxFailures">Consecutive failures before the circuit opens.</param>
    /// <param name="cooldownMs">Milliseconds to wait before allowing a half-open probe.</param>
    /// <param name="tickProvider">
    /// Monotonic clock source (milliseconds). Defaults to <see cref="Environment.TickCount64"/>.
    /// Inject a custom provider in tests to control time.
    /// </param>
    public CircuitBreaker(
        int maxFailures = 3,
        long cooldownMs = 60_000,
        Func<long>? tickProvider = null)
    {
        _maxFailures = maxFailures;
        _cooldownMs = cooldownMs;
        _tickProvider = tickProvider ?? (() => Environment.TickCount64);
    }

    /// <summary>True when the circuit is open and cooldown has not elapsed (consistent with <see cref="AllowRequest"/>).</summary>
    public bool IsOpen => !AllowRequest();

    /// <summary>
    /// Returns true if the caller is allowed to make a request.
    /// Closed circuit: always true.
    /// Open circuit: true only when the cooldown has elapsed (half-open probe).
    /// </summary>
    public bool AllowRequest()
    {
        var failures = Interlocked.Read(ref _consecutiveFailures);
        if (failures < _maxFailures)
            return true;

        // Circuit is open — check if cooldown has elapsed
        var openedAt = Interlocked.Read(ref _openedAtTick);
        var elapsed = _tickProvider() - openedAt;
        return elapsed > _cooldownMs;
    }

    /// <summary>Records a successful request. Resets the failure counter and closes the circuit.</summary>
    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    /// <summary>
    /// Records a failed request. Increments the failure counter and opens the circuit
    /// when the threshold is reached.
    /// </summary>
    public void RecordFailure()
    {
        var newCount = Interlocked.Increment(ref _consecutiveFailures);
        if (newCount >= _maxFailures)
        {
            Interlocked.Exchange(ref _openedAtTick, _tickProvider());
        }
    }
}
