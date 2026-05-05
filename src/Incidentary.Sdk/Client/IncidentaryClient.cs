using System.Diagnostics;
using Incidentary.Sdk.Buffering;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.Integrations;
using Incidentary.Sdk.PreArm;
using Incidentary.Sdk.Redaction;
using Incidentary.Sdk.TraceCap;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using Microsoft.Extensions.Logging;
using TC = Incidentary.Sdk.TraceCap.TraceCap;

namespace Incidentary.Sdk;

/// <summary>
/// The core Incidentary SDK client. Thread-safe for concurrent use.
/// All public methods are fail-open — they never throw into user code.
/// Implements IDisposable and IAsyncDisposable for graceful shutdown.
/// </summary>
public sealed partial class IncidentaryClient : IIncidentaryClient
{
    private readonly IncidentaryClientOptions _options;
    private readonly RingBuffer<CausalEvent> _buffer;
    private readonly FlushQueue _flushQueue;
    private readonly ITransport _transport;
    private readonly PreArmEngine _preArmEngine;
    private readonly IntegrationRegistry? _integrationRegistry;
    private readonly ILogger<IncidentaryClient>? _logger;
    private readonly long _startTicks = Environment.TickCount64;
    private readonly HttpClient? _ownedHttpClient; // non-null only when created by this constructor
    private int _disposed;

    // ─── Adaptive batch sizing ──────────────────────────────────────────
    private const double EmaAlpha = 0.3;
    private const int MinBatchSize = 10;
    private const int MaxBatchSize = 5_000;
    private const int DefaultBatchSize = 500;

    private double _flushLatencyEma;
    private int _currentBatchSize = DefaultBatchSize;
    private readonly object _adaptiveLock = new();

    // ─── L1 Trace Cap ───────────────────────────────────────────────────
    // Drops bytes before they hit the buffer when a single service emits
    // a runaway trace. Spec at docs/specs/l1-trace-cap.md (in main repo).
    private readonly TC _traceCap;
    private long _traceCapDroppedTotal;

    /// <summary>Creates a new Incidentary client with the specified options.</summary>
    public IncidentaryClient(IncidentaryClientOptions options, ILogger<IncidentaryClient>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ApiKey must not be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new ArgumentException("ServiceName must not be empty.", nameof(options));

        _logger = logger;

        if (options.BufferCapacity <= 0)
            throw new ArgumentException("BufferCapacity must be greater than zero.", nameof(options));
        if (options.TimeoutMs <= 0)
            throw new ArgumentException("TimeoutMs must be greater than zero.", nameof(options));

        _buffer = new RingBuffer<CausalEvent>(options.BufferCapacity);

        var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs) };
        _ownedHttpClient = httpClient; // we created it, we own it
        if (!string.IsNullOrEmpty(options.BaseUrl))
        {
            var uri = new Uri(options.BaseUrl);
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "BaseUrl must use HTTPS to protect the API key in transit. " +
                    "HTTP is not permitted.",
                    nameof(options));
            httpClient.BaseAddress = uri;
        }

        _transport = new HttpTransport(
            httpClient,
            options.ApiKey,
            options.ServiceName,
            options.Environment,
            options.WorkspaceId,
            options.TimeoutMs,
            options.OnError,
            logger: null);

        _flushQueue = new FlushQueue(
            _transport,
            options.OnError);

        _preArmEngine = new PreArmEngine(
            options,
            onModeChanged: OnModeChanged);

        _traceCap = new TC(new TraceCapOptions
        {
            ServiceId = options.ServiceName,
            Enabled = options.TraceCapEnabled,
        });

        if (options.AutoInstrument)
        {
            _integrationRegistry = new IntegrationRegistry(logger, options.OnError);
        }
    }

    /// <summary>Creates a client with an externally provided transport (for testing).</summary>
    internal IncidentaryClient(
        IncidentaryClientOptions options,
        ITransport transport,
        ILogger<IncidentaryClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _transport = transport;

        _buffer = new RingBuffer<CausalEvent>(options.BufferCapacity);

        _flushQueue = new FlushQueue(
            transport,
            options.OnError);

        _preArmEngine = new PreArmEngine(
            options,
            onModeChanged: OnModeChanged);

        _traceCap = new TC(new TraceCapOptions
        {
            ServiceId = options.ServiceName,
            Enabled = options.TraceCapEnabled,
        });
    }

    /// <inheritdoc />
    public ClientCaptureMode CaptureMode => _preArmEngine.Mode;

    /// <summary>Current EMA of flush latency in milliseconds. Updated after each successful flush.</summary>
    public double FlushLatencyEmaMs
    {
        get { lock (_adaptiveLock) return _flushLatencyEma; }
    }

    /// <summary>Current adaptive batch size (10..5000). Used as the max batch size for the next flush.</summary>
    public int CurrentBatchSize
    {
        get { lock (_adaptiveLock) return _currentBatchSize; }
    }

    /// <inheritdoc />
    public bool ShouldCaptureDetail =>
        _options.DetailCaptureEnabled && CaptureMode != ClientCaptureMode.Normal;

    /// <inheritdoc />
    public void RecordRequest(int statusCode, RecordRequestOptions? options = null)
    {
        try
        {
            var kind = options?.Kind ?? CeKind.HttpIn;
            var eventType = options?.EventType ?? MapKindToEventType(kind);
            var traceId = options?.TraceId ?? IncidentaryActivity.Current?.TraceId ?? Guid.NewGuid().ToString();
            var parentCeId = options?.ParentCeId ?? IncidentaryActivity.Current?.CeId;
            var durationNs = options?.DurationNs ?? 0;

            var ce = new CausalEvent
            {
                Id = Guid.NewGuid().ToString(),
                TraceId = traceId,
                ParentId = parentCeId,
                ServiceId = _options.ServiceName,
                OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
                Kind = kind,
                Type = eventType,
                StatusCode = statusCode,
                DurationNs = durationNs,
                Attributes = EventAttrsSanitizer.Sanitize(options?.EventAttrs),
                Detail = ShouldCaptureDetail ? BuildDetail(options) : null
            };

            WriteEvent(ce);

            // Feed triggers
            long durationMs = durationNs / 1_000_000;
            _preArmEngine.OnRequestCompleted(statusCode, durationMs, edgeKeyHash: null, isRetry: false);
            _preArmEngine.OnRequestEnded();
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public void RecordRequestStart(CeKind kind = CeKind.HttpIn)
    {
        try
        {
            _preArmEngine.OnRequestStarted();
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public void RecordEvent(string eventType, RecordEventOptions? options = null)
    {
        try
        {
            var kind = options?.Kind ?? CeKind.Internal;
            var traceId = options?.TraceId ?? IncidentaryActivity.Current?.TraceId ?? Guid.NewGuid().ToString();
            var parentCeId = options?.ParentCeId ?? IncidentaryActivity.Current?.CeId;

            var attrs = options?.EventAttrs != null
                ? new Dictionary<string, object>(options.EventAttrs)
                : new Dictionary<string, object>();

            if (options?.Topic is not null)
            {
                attrs["topic"] = options.Topic;
            }

            var ce = new CausalEvent
            {
                Id = Guid.NewGuid().ToString(),
                TraceId = traceId,
                ParentId = parentCeId,
                ServiceId = _options.ServiceName,
                OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
                Kind = kind,
                Type = eventType,
                StatusCode = options?.Status ?? 200,
                DurationNs = options?.DurationNs ?? 0,
                Attributes = EventAttrsSanitizer.Sanitize(attrs.Count > 0 ? attrs : null)
            };

            WriteEvent(ce);
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public void RecordQueuePublish(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.QueuePublish, WithKind(options, CeKind.QueuePublish));

    /// <inheritdoc />
    public void RecordQueueConsume(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.QueueConsume, WithKind(options, CeKind.QueueConsume));

    /// <inheritdoc />
    public void RecordJobStart(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.JobStart, options);

    /// <inheritdoc />
    public void RecordJobEnd(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.JobEnd, options);

    /// <inheritdoc />
    public void RecordWebhookIn(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.WebhookIn, WithKind(options, CeKind.HttpIn));

    /// <inheritdoc />
    public void RecordWebhookOut(RecordEventOptions? options = null) =>
        RecordEvent(EventTypes.WebhookOut, WithKind(options, CeKind.HttpOut));

    /// <inheritdoc />
    public void WriteEvent(CausalEvent ce)
    {
        try
        {
            var verdict = _traceCap.Observe(ce.TraceId);
            if (verdict.ShouldDrop)
            {
                Interlocked.Increment(ref _traceCapDroppedTotal);
                return;
            }
            if (verdict.Tier == VerdictTier.Truncating)
            {
                // Boundary span — mark it so downstream UIs can show the
                // truncation point. All later spans for this trace drop
                // before reaching this method.
                var attrs = ce.Attributes is null
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object>(ce.Attributes);
                attrs["incidentary.trace.truncated_in_sdk"] = true;
                ce = new CausalEvent
                {
                    Id = ce.Id,
                    TraceId = ce.TraceId,
                    ParentId = ce.ParentId,
                    SpanId = ce.SpanId,
                    ServiceId = ce.ServiceId,
                    OccurredAt = ce.OccurredAt,
                    Kind = ce.Kind,
                    Type = ce.Type,
                    StatusCode = ce.StatusCode,
                    Severity = ce.Severity,
                    DurationNs = ce.DurationNs,
                    Attributes = attrs,
                    Detail = ce.Detail,
                    CapturedBeforeAlert = ce.CapturedBeforeAlert,
                    RingBufferSeq = ce.RingBufferSeq,
                };
            }

            _buffer.Write(ce);
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <summary>
    /// Register a callback invoked at most once per (trace_id, tier) when
    /// the L1 trace cap detects a runaway trace in this client.
    /// Useful for routing the structured signal into the customer's
    /// existing observability pipeline.
    /// </summary>
    public void OnTraceCapTransition(Action<TraceCapEvent> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _traceCap.SetHook(hook);
    }

    /// <summary>
    /// Cumulative count of spans dropped at L1 by this client.
    /// </summary>
    public long TraceCapDroppedTotal => Interlocked.Read(ref _traceCapDroppedTotal);

    /// <inheritdoc />
    public async Task FlushToBackendAsync(string? incidentId = null, CancellationToken ct = default)
    {
        try
        {
            var events = _buffer.Flush();
            if (events.Count == 0) return;

            // Annotate events
            long seq = 0;
            var annotated = new List<CausalEvent>(events.Count);
            foreach (var ce in events)
            {
                annotated.Add(new CausalEvent
                {
                    Id = ce.Id,
                    TraceId = ce.TraceId,
                    ParentId = ce.ParentId,
                    SpanId = ce.SpanId,
                    ServiceId = ce.ServiceId,
                    OccurredAt = ce.OccurredAt,
                    Kind = ce.Kind,
                    Type = ce.Type,
                    StatusCode = ce.StatusCode,
                    Severity = ce.Severity,
                    DurationNs = ce.DurationNs,
                    Attributes = ce.Attributes,
                    Detail = ce.Detail,
                    CapturedBeforeAlert = CaptureMode != ClientCaptureMode.Incident,
                    RingBufferSeq = seq++
                });
            }

            string captureMode = CaptureMode == ClientCaptureMode.Normal
                ? CaptureModes.Skeleton
                : CaptureModes.Full;

            // Update transport telemetry fields before flushing
            if (_transport is HttpTransport httpTransport)
            {
                lock (_adaptiveLock)
                {
                    httpTransport.FlushLatencyEmaMs = _flushLatencyEma;
                    httpTransport.CurrentBatchSize = _currentBatchSize;
                }
            }

            var flushedBefore = _flushQueue.TotalFlushed;
            var sw = Stopwatch.StartNew();

            var requestedCaptureMode = await _flushQueue.FlushAsync(annotated, captureMode, incidentId, ct).ConfigureAwait(false);

            sw.Stop();

            // Only update EMA and batch size if at least some events were flushed successfully
            var flushedAfter = _flushQueue.TotalFlushed;
            if (flushedAfter > flushedBefore)
            {
                UpdateAdaptiveBatchSize(sw.Elapsed.TotalMilliseconds);
            }

            if (requestedCaptureMode is not null && _logger is not null)
            {
                LogCaptureModeRequested(_logger, requestedCaptureMode);
            }
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public void EscalateToIncident(string? incidentId = null)
    {
        try
        {
            _preArmEngine.EscalateToIncident(incidentId);
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public void CloseIncident()
    {
        try
        {
            _preArmEngine.CloseIncident();
        }
        catch (Exception ex)
        {
            SafeOnError(ex);
        }
    }

    /// <inheritdoc />
    public PreArmDebugState GetPreArmDebugState() => _preArmEngine.GetDebugState();

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _integrationRegistry?.Dispose();
        _transport.Dispose();
        _ownedHttpClient?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // Flush pending events with a 5-second timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await FlushToBackendAsync(ct: cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best effort
        }

        _integrationRegistry?.Dispose();
        await _flushQueue.DisposeAsync().ConfigureAwait(false);
        _transport.Dispose();
        _ownedHttpClient?.Dispose();
    }

    private void OnModeChanged(ClientCaptureMode newMode)
    {
        if (_logger is not null)
        {
            LogModeChanged(_logger, newMode.ToString());
        }
    }

    private CeDetail? BuildDetail(RecordRequestOptions? options)
    {
        if (options is null) return null;

        string? errorClassification = options.Cancelled ? "cancelled"
            : options.TimedOut ? "timeout"
            : null;

        return new CeDetail
        {
            Method = options.Method,
            RouteTemplate = options.RouteTemplate,
            RequestBytes = options.RequestBytes,
            ResponseBytes = options.ResponseBytes,
            RequestHeaders = FilterHeaders(
                options.RequestHeaders,
                _options.RequestHeaderAllowlist ?? IncidentaryClientOptions.DefaultRequestHeaderAllowlist),
            ResponseHeaders = FilterHeaders(
                options.ResponseHeaders,
                _options.ResponseHeaderAllowlist ?? IncidentaryClientOptions.DefaultResponseHeaderAllowlist),
            LocalErrorClassification = errorClassification
        };
    }

    private static Dictionary<string, string>? FilterHeaders(
        Dictionary<string, string>? headers,
        IReadOnlyList<string> allowlist)
    {
        if (headers is null || headers.Count == 0) return null;
        var allowed = new HashSet<string>(allowlist, StringComparer.OrdinalIgnoreCase);
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            if (allowed.Contains(key))
                filtered[key] = value;
        }
        return filtered.Count > 0 ? filtered : null;
    }

    private static string MapKindToEventType(CeKind kind) => kind switch
    {
        CeKind.HttpIn => EventTypes.HttpServer,
        CeKind.HttpOut => EventTypes.HttpClient,
        CeKind.QueuePublish => EventTypes.QueuePublish,
        CeKind.QueueConsume => EventTypes.QueueConsume,
        CeKind.Internal => EventTypes.InternalTask,
        CeKind.DbQuery => EventTypes.DbQuery,
        CeKind.Job => EventTypes.JobStart,
        _ => EventTypes.InternalTask
    };

    private static RecordEventOptions WithKind(RecordEventOptions? options, CeKind kind)
    {
        if (options is null) return new RecordEventOptions { Kind = kind };
        return new RecordEventOptions
        {
            Kind = kind,
            Status = options.Status,
            DurationNs = options.DurationNs,
            TraceId = options.TraceId,
            ParentCeId = options.ParentCeId,
            EventAttrs = options.EventAttrs,
            Topic = options.Topic
        };
    }

    private void UpdateAdaptiveBatchSize(double latencyMs)
    {
        lock (_adaptiveLock)
        {
            // Update EMA: seed on first measurement, smooth thereafter
            _flushLatencyEma = _flushLatencyEma == 0.0
                ? latencyMs
                : EmaAlpha * latencyMs + (1.0 - EmaAlpha) * _flushLatencyEma;

            var ceiling = _options.MaxFlushOverheadMs;

            if (_flushLatencyEma < ceiling * 0.5)
            {
                // Latency well below ceiling — increase batch size by 20%
                _currentBatchSize = Math.Min(MaxBatchSize, (int)(_currentBatchSize * 1.2));
            }
            else if (_flushLatencyEma > ceiling * 0.9)
            {
                // Latency near ceiling — decrease batch size by 30%
                _currentBatchSize = Math.Max(MinBatchSize, (int)(_currentBatchSize * 0.7));
            }
        }
    }

    private void SafeOnError(Exception ex)
    {
        try { _options.OnError?.Invoke(ex); } catch { /* swallow */ }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Incidentary capture mode changed to {Mode}")]
    private static partial void LogModeChanged(ILogger logger, string mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend requested capture mode: {RequestedMode}")]
    private static partial void LogCaptureModeRequested(ILogger logger, string requestedMode);
}
