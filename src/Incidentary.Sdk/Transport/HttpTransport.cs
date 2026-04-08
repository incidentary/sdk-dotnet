using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Incidentary.Sdk.WireFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incidentary.Sdk.Transport;

/// <summary>
/// Sends causal event batches to the Incidentary backend over HTTP.
/// Includes a circuit breaker and quota-pause mechanism.
/// Never throws exceptions into user code.
/// </summary>
public sealed partial class HttpTransport : ITransport
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _serviceName;
    private readonly string _environment;
    private readonly string _workspaceId;
    private readonly Action<Exception>? _onError;
    private readonly ILogger<HttpTransport> _logger;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly QuotaPause _quotaPause;
    private int _disposed;

    public HttpTransport(
        HttpClient httpClient,
        string apiKey,
        string serviceName,
        string environment,
        string? workspaceId = null,
        int timeoutMs = 5000,
        Action<Exception>? onError = null,
        ILogger<HttpTransport>? logger = null)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _serviceName = serviceName;
        _environment = environment;
        _workspaceId = workspaceId ?? string.Empty;
        _onError = onError;
        _logger = logger ?? NullLogger<HttpTransport>.Instance;
        _circuitBreaker = new CircuitBreaker();
        _quotaPause = new QuotaPause();

        if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
            _httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
    }

    /// <inheritdoc />
    public bool IsHealthy =>
        !_circuitBreaker.IsOpen
        && !_quotaPause.IsPaused
        && _httpClient.BaseAddress is not null;

    /// <inheritdoc />
    public async Task<bool> UploadBatchAsync(
        IReadOnlyList<CausalEvent> events,
        string captureMode,
        string? incidentId = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_circuitBreaker.AllowRequest())
                return false;

            if (_quotaPause.IsPaused)
                return false;

            var batch = new IngestBatch
            {
                SchemaVersion = "1",
                WorkspaceId = _workspaceId,
                ServiceId = _serviceName,
                Environment = _environment,
                FlushedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                CaptureMode = captureMode,
                Events = events,
                SdkTelemetry = new SdkTelemetry
                {
                    SdkVersion = SdkVersion.Current
                }
            };

            var json = JsonSerializer.Serialize(batch, WireJson.Options);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/batch")
            {
                Content = content
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Add("X-Incidentary-SDK-Version", SdkVersion.Current);

            if (incidentId is not null)
                request.Headers.Add("X-Incidentary-Incident-Id", incidentId);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordSuccess();
                return true;
            }

            var statusCode = (int)response.StatusCode;

            // 426 Upgrade Required — SDK version too old
            if (statusCode == 426)
            {
                LogSdkVersionTooOld(_logger, SdkVersion.Current);
                // Treat as success (don't retry, don't trip circuit)
                return true;
            }

            // 429 with ce_limit_reached — pause until next month
            if (statusCode == 429)
            {
                var reason = response.Headers.Contains("X-Incidentary-Reason")
                    ? response.Headers.GetValues("X-Incidentary-Reason").FirstOrDefault()
                    : null;

                if (reason == "ce_limit_reached")
                {
                    _quotaPause.PauseUntilNextMonth();
                    LogQuotaPaused(_logger, _quotaPause.ResumeAt?.ToString("u") ?? "unknown");
                    return false;
                }
            }

            // Other 4xx/5xx — record circuit breaker failure
            _circuitBreaker.RecordFailure();
            return false;
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _onError?.Invoke(ex);
            LogUploadBatchFailed(_logger, ex);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task NotifyBackendAsync(
        string eventType,
        string serviceId,
        IDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["event"] = eventType,
                ["service_id"] = serviceId
            };

            if (metadata is not null)
            {
                foreach (var kvp in metadata)
                    payload[kvp.Key] = kvp.Value;
            }

            var json = JsonSerializer.Serialize(payload, WireJson.Options);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/services/events")
            {
                Content = content
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Add("X-Incidentary-SDK-Version", SdkVersion.Current);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var ex = new HttpRequestException(
                    $"Incidentary notification rejected: {(int)response.StatusCode} {response.ReasonPhrase}");
                LogNotifyBackendFailed(_logger, eventType, serviceId, ex);
                _onError?.Invoke(ex);
            }
        }
        catch (Exception ex)
        {
            LogNotifyBackendFailed(_logger, eventType, serviceId, ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        // HttpClient lifetime is owned by the caller; do not dispose it here.
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SDK version {Version} is too old — server returned 426. Consider upgrading.")]
    private static partial void LogSdkVersionTooOld(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Causal event quota reached. Ingestion paused until {ResumeAt}.")]
    private static partial void LogQuotaPaused(ILogger logger, string resumeAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to upload batch to Incidentary backend.")]
    private static partial void LogUploadBatchFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to notify backend (event={EventType}, service={ServiceId}).")]
    private static partial void LogNotifyBackendFailed(ILogger logger, string eventType, string serviceId, Exception ex);
}
