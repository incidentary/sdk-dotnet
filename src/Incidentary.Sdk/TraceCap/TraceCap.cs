// L1 — SDK-side per-trace span cap.
//
// Cross-SDK spec: docs/specs/l1-trace-cap.md (in the main incidentary repo).
// Threshold parity is mandatory:
//   - apps/api/src/billing/trace_meter.rs (Rust API)
//   - processor/incidentaryprocessor/trace_breaker.go (Bridge)
//   - SDKs: Node, Python, Go share these same constants.
//
// Catches single-process runaway traces at the source. Memory bounded
// by an LRU on counters (default 1024) and breaker blacklist (256).

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Incidentary.Sdk.TraceCap;

/// <summary>
/// Per-SDK-instance, per-trace span cap.
/// </summary>
/// <remarks>
/// Spec: docs/specs/l1-trace-cap.md.
/// Threshold parity is mandatory across all SDKs and the API.
/// </remarks>
public sealed class TraceCap
{
    /// <summary>5K spans/trace — log warn, no behavior change.</summary>
    public const long SpansPerTraceWarn = 5_000;

    /// <summary>50K spans/trace — drop subsequent spans for the trace.</summary>
    public const long SpansPerTraceTruncate = 50_000;

    /// <summary>500K spans/trace — blacklist trace_id for the rest of the LRU window.</summary>
    public const long SpansPerTraceBreaker = 500_000;

    private const int DefaultMaxTrackedTraces = 1_024;
    private const int DefaultMaxBlacklistedTraces = 256;

    private readonly string _serviceId;
    private readonly bool _enabled;
    private readonly BoundedLru<string, long> _counters;
    private readonly BoundedLru<string, bool> _blacklist;
    private readonly BoundedLru<string, bool> _transitions;
    private readonly object _hookLock = new();
    private Action<TraceCapEvent> _hook;

    public TraceCap(TraceCapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ServiceId))
            throw new ArgumentException("ServiceId is required", nameof(options));
        _serviceId = options.ServiceId;
        _enabled = options.Enabled;
        _hook = options.Hook ?? DefaultHook;
        var maxTraces = options.MaxTrackedTraces > 0 ? options.MaxTrackedTraces : DefaultMaxTrackedTraces;
        var maxBlacklist = options.MaxBlacklistedTraces > 0 ? options.MaxBlacklistedTraces : DefaultMaxBlacklistedTraces;
        _counters = new BoundedLru<string, long>(maxTraces);
        _blacklist = new BoundedLru<string, bool>(maxBlacklist);
        _transitions = new BoundedLru<string, bool>(maxTraces * 3);
    }

    /// <summary>
    /// Apply the cap to a single span attempt.
    /// </summary>
    public Verdict Observe(string? traceId)
    {
        if (!_enabled || string.IsNullOrEmpty(traceId)) return Verdict.AcceptNone;

        if (_blacklist.ContainsKey(traceId)) return Verdict.DropBreaker;

        var prior = _counters.TryGet(traceId, out var v) ? v : 0L;
        var next = prior + 1;
        _counters.Set(traceId, next);

        if (next >= SpansPerTraceBreaker)
        {
            if (next == SpansPerTraceBreaker)
            {
                _blacklist.Set(traceId, true);
                _counters.Remove(traceId);
                EmitOnce(traceId, TraceCapTier.Breaker, next);
            }
            return Verdict.DropBreaker;
        }
        if (next > SpansPerTraceTruncate) return Verdict.DropTruncate;
        if (next == SpansPerTraceTruncate)
        {
            EmitOnce(traceId, TraceCapTier.Truncate, next);
            return Verdict.AcceptTruncating;
        }
        if (next == SpansPerTraceWarn)
        {
            EmitOnce(traceId, TraceCapTier.Warn, next);
            return Verdict.AcceptWarn;
        }
        return Verdict.AcceptNone;
    }

    /// <summary>
    /// Replace the tier-transition hook. Safe to call after observe()
    /// has begun.
    /// </summary>
    public void SetHook(Action<TraceCapEvent> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        lock (_hookLock) { _hook = hook; }
    }

    /// <summary>Test seam: snapshot of active counter map size.</summary>
    public int TrackedTraceCount => _counters.Count;

    /// <summary>Test seam: blacklist size.</summary>
    public int BlacklistedTraceCount => _blacklist.Count;

    private void EmitOnce(string traceId, TraceCapTier tier, long count)
    {
        var key = traceId + "|" + tier.ToString();
        if (_transitions.ContainsKey(key)) return;
        _transitions.Set(key, true);
        Action<TraceCapEvent> hook;
        lock (_hookLock) { hook = _hook; }
        try
        {
            hook(new TraceCapEvent(
                tier,
                traceId,
                count,
                _serviceId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        catch
        {
            // Hook is customer-controllable; never propagate.
        }
    }

    private static void DefaultHook(TraceCapEvent ev)
    {
        try
        {
            var payload = new
            {
                @event = "incidentary_trace_cap_tier",
                tier = ev.Tier.ToString().ToLowerInvariant(),
                trace_id = ev.TraceId,
                cumulative_span_count = ev.CumulativeSpanCount,
                service_id = ev.ServiceId,
                timestamp_ms = ev.TimestampMs,
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(payload));
        }
        catch { /* best-effort logging */ }
    }
}

/// <summary>Configuration for <see cref="TraceCap"/>.</summary>
public sealed class TraceCapOptions
{
    public required string ServiceId { get; init; }
    public Action<TraceCapEvent>? Hook { get; init; }
    public bool Enabled { get; init; } = true;
    public int MaxTrackedTraces { get; init; }
    public int MaxBlacklistedTraces { get; init; }
}

/// <summary>Identifies which threshold a trace has crossed.</summary>
public enum TraceCapTier
{
    Warn,
    Truncate,
    Breaker,
}

/// <summary>Structured payload emitted on every tier transition.</summary>
public sealed record TraceCapEvent(
    TraceCapTier Tier,
    string TraceId,
    long CumulativeSpanCount,
    string ServiceId,
    long TimestampMs);

/// <summary>
/// Verdict returned by <see cref="TraceCap.Observe"/>. ShouldDrop is the
/// primary signal; Tier/Reason describe why for telemetry.
/// </summary>
public sealed record Verdict(bool ShouldDrop, VerdictTier Tier = VerdictTier.None, VerdictReason Reason = VerdictReason.None)
{
    public static readonly Verdict AcceptNone = new(false, VerdictTier.None);
    public static readonly Verdict AcceptWarn = new(false, VerdictTier.Warn);
    public static readonly Verdict AcceptTruncating = new(false, VerdictTier.Truncating);
    public static readonly Verdict DropTruncate = new(true, VerdictTier.None, VerdictReason.Truncate);
    public static readonly Verdict DropBreaker = new(true, VerdictTier.None, VerdictReason.Breaker);
}

public enum VerdictTier { None, Warn, Truncating }
public enum VerdictReason { None, Truncate, Breaker }

/// <summary>O(1) bounded LRU built on LinkedList + Dictionary.</summary>
internal sealed class BoundedLru<TKey, TValue> where TKey : notnull
{
    private readonly int _max;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _lookup;
    private readonly object _lock = new();

    public BoundedLru(int max)
    {
        _max = max;
        _lookup = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(max);
    }

    public int Count { get { lock (_lock) return _order.Count; } }

    public bool ContainsKey(TKey key)
    {
        lock (_lock) return _lookup.ContainsKey(key);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddLast(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                var replaced = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(key, value));
                _order.AddLast(replaced);
                _lookup[key] = replaced;
                return;
            }
            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                new KeyValuePair<TKey, TValue>(key, value));
            _order.AddLast(node);
            _lookup[key] = node;
            if (_order.Count > _max)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _lookup.Remove(oldest.Value.Key);
            }
        }
    }

    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _lookup.Remove(key);
                return true;
            }
            return false;
        }
    }
}
