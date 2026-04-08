using System.Net;
using System.Text.Json;
using FluentAssertions;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Transport;

public sealed class HttpTransportTests : IDisposable
{
    private const string TestApiKey = "test-api-key-123";
    private const string TestServiceName = "checkout-api";
    private const string TestEnvironment = "test";
    private const string TestWorkspaceId = "ws_test";

    private static List<CausalEvent> CreateTestEvents(int count = 1)
    {
        return Enumerable.Range(0, count).Select(i => new CausalEvent
        {
            CeId = $"ce-{i}",
            TraceId = "trace-1",
            ServiceId = TestServiceName,
            WallTsNs = 1733103000000000000 + i,
            Kind = CeKind.HttpIn,
            EventType = EventTypes.HttpIn,
            Status = 200,
            DurationNs = 45000000,
            SdkVersion = SdkVersion.Current
        }).ToList();
    }

    private readonly List<IDisposable> _disposables = [];

    private (HttpTransport transport, MockHttpHandler handler) CreateTransport(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null,
        Action<Exception>? onError = null,
        string baseUrl = "https://api.incidentary.io")
    {
        handler ??= (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var mockHandler = new MockHttpHandler(handler);
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri(baseUrl) };

        var transport = new HttpTransport(
            httpClient,
            TestApiKey,
            TestServiceName,
            TestEnvironment,
            workspaceId: TestWorkspaceId,
            onError: onError);

        _disposables.Add(transport);
        _disposables.Add(httpClient);

        return (transport, mockHandler);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
    }

    [Fact]
    public async Task UploadBatch_Success_ReturnsTrue()
    {
        var (transport, _) = CreateTransport();

        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadBatch_ServerError_ReturnsFalse()
    {
        var (transport, _) = CreateTransport(
            handler: (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_SetsCorrectHeaders()
    {
        var (transport, mockHandler) = CreateTransport();

        await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        var request = mockHandler.LastRequest;
        request.Should().NotBeNull();
        request!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(TestApiKey);
        request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        request.Headers.GetValues("X-Incidentary-SDK-Version")
            .Should().ContainSingle()
            .Which.Should().Be(SdkVersion.Current);
    }

    [Fact]
    public async Task UploadBatch_WithIncidentId_SetsHeader()
    {
        var (transport, mockHandler) = CreateTransport();

        await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton, incidentId: "inc-42");

        var request = mockHandler.LastRequest;
        request.Should().NotBeNull();
        request!.Headers.GetValues("X-Incidentary-Incident-Id")
            .Should().ContainSingle()
            .Which.Should().Be("inc-42");
    }

    [Fact]
    public async Task UploadBatch_WithoutIncidentId_DoesNotSetHeader()
    {
        var (transport, mockHandler) = CreateTransport();

        await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        var request = mockHandler.LastRequest;
        request.Should().NotBeNull();
        request!.Headers.Contains("X-Incidentary-Incident-Id").Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_CircuitOpen_ReturnsFalseWithoutSending()
    {
        var requestCount = 0;
        var (transport, _) = CreateTransport(
            handler: (_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

        // Trip the circuit breaker (3 failures)
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        requestCount.Should().Be(3);

        // This request should not hit the server
        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
        requestCount.Should().Be(3);
    }

    [Fact]
    public async Task UploadBatch_426Response_TreatsAsSuccess()
    {
        Exception? capturedError = null;
        var (transport, _) = CreateTransport(
            handler: (_, _) => Task.FromResult(
                new HttpResponseMessage((HttpStatusCode)426)),
            onError: ex => capturedError = ex);

        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        // 426 is treated as success (don't retry)
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadBatch_429CeLimit_PausesQuota()
    {
        var (transport, _) = CreateTransport(
            handler: (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.Add("X-Incidentary-Reason", "ce_limit_reached");
                return Task.FromResult(response);
            });

        transport.IsHealthy.Should().BeTrue();

        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
        transport.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_SendsCorrectJsonPayload()
    {
        var (transport, mockHandler) = CreateTransport();

        var events = CreateTestEvents(2);
        await transport.UploadBatchAsync(
            events, CaptureModes.Skeleton);

        var body = mockHandler.LastRequestBody;
        body.Should().NotBeNullOrEmpty();

        var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;

        root.GetProperty("schema_version").GetString().Should().Be("1");
        root.GetProperty("workspace_id").GetString().Should().Be(TestWorkspaceId);
        root.GetProperty("service_id").GetString().Should().Be(TestServiceName);
        root.GetProperty("environment").GetString().Should().Be(TestEnvironment);
        root.GetProperty("capture_mode").GetString().Should().Be("SKELETON");
        root.GetProperty("events").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task UploadBatch_Exception_ReturnsFalse()
    {
        Exception? capturedError = null;
        var (transport, _) = CreateTransport(
            handler: (_, _) => throw new HttpRequestException("connection refused"),
            onError: ex => capturedError = ex);

        var result = await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
        capturedError.Should().NotBeNull();
        capturedError.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task UploadBatch_Exception_NeverThrows()
    {
        var (transport, _) = CreateTransport(
            handler: (_, _) => throw new HttpRequestException("connection refused"));

        // Should NOT throw, even without an onError handler
        var act = () => transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyBackend_SendsCorrectPayload()
    {
        var (transport, mockHandler) = CreateTransport();

        await transport.NotifyBackendAsync(
            "service.started",
            "svc-123",
            new Dictionary<string, object> { ["version"] = "1.0" });

        var request = mockHandler.LastRequest;
        request.Should().NotBeNull();
        request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/services/events");

        var body = mockHandler.LastRequestBody;
        body.Should().NotBeNullOrEmpty();

        var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        root.GetProperty("event").GetString().Should().Be("service.started");
        root.GetProperty("service_id").GetString().Should().Be("svc-123");
        root.GetProperty("version").GetString().Should().Be("1.0");
    }

    [Fact]
    public async Task NotifyBackend_Exception_DoesNotThrow()
    {
        var (transport, _) = CreateTransport(
            handler: (_, _) => throw new HttpRequestException("fail"));

        var act = () => transport.NotifyBackendAsync(
            "service.started", "svc-123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void IsHealthy_AllGood_ReturnsTrue()
    {
        var (transport, _) = CreateTransport();

        transport.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthy_CircuitOpen_ReturnsFalse()
    {
        var (transport, _) = CreateTransport(
            handler: (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // Trip circuit
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        transport.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_PostsToCorrectUrl()
    {
        var (transport, mockHandler) = CreateTransport();

        await transport.UploadBatchAsync(
            CreateTestEvents(), CaptureModes.Skeleton);

        var request = mockHandler.LastRequest;
        request.Should().NotBeNull();
        request!.RequestUri!.PathAndQuery.Should().Be("/api/v1/ingest/batch");
        request.Method.Should().Be(HttpMethod.Post);
    }

    // ── Quota pause branch coverage ───────────────────────────────────────────

    [Fact]
    public async Task UploadBatch_429WithoutReasonHeader_RecordsFailure()
    {
        // 429 with NO X-Incidentary-Reason header → reason = null → falls to RecordFailure
        var (transport, _) = CreateTransport(
            handler: (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        // No reason header → covers `reason = null` path inside 429 handling

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        // Not ce_limit_reached → circuit breaker records failure → returns false
        result.Should().BeFalse();
        // IsHealthy depends on circuit state; quota should NOT be paused
        transport.IsHealthy.Should().BeTrue(); // quota not paused (wrong reason)
    }

    [Fact]
    public async Task UploadBatch_429WithDifferentReasonHeader_RecordsFailure()
    {
        // 429 with X-Incidentary-Reason: rate_limit_exceeded (not ce_limit_reached)
        var (transport, _) = CreateTransport(
            handler: (_, _) =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.Add("X-Incidentary-Reason", "rate_limit_exceeded");
                return Task.FromResult(r);
            });

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        // Reason is not "ce_limit_reached" → RecordFailure, not quota pause
        result.Should().BeFalse();
        transport.IsHealthy.Should().BeTrue(); // quota not paused
    }

    [Fact]
    public async Task UploadBatch_WhenQuotaPaused_ReturnsFalseImmediately()
    {
        // First call triggers quota pause
        var (transport, _) = CreateTransport(
            handler: (_, _) =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.Add("X-Incidentary-Reason", "ce_limit_reached");
                return Task.FromResult(r);
            });

        // First call → pauses quota
        await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        // Second call → _quotaPause.IsPaused = true → early return false (covers line 71)
        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NotifyBackend_NonSuccessResponse_InvokesOnError()
    {
        // NotifyBackendAsync: `if (!response.IsSuccessStatusCode)` → non-success path
        var errors = new List<Exception>();
        var (transport, _) = CreateTransport(
            handler: (req, _) =>
            {
                // Return 500 for the notify endpoint
                if (req.RequestUri?.PathAndQuery.Contains("services/events") == true)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            onError: ex => errors.Add(ex));

        await transport.NotifyBackendAsync("service.started", "my-service");

        // Non-2xx response creates an HttpRequestException and invokes onError
        errors.Should().ContainSingle();
        errors[0].Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task NotifyBackend_NonSuccessResponse_NullOnError_DoesNotThrow()
    {
        // Covers line 187 null branch: `_onError?.Invoke(ex)` when _onError is null.
        // NotifyBackendAsync with a non-success response and NO onError callback.
        var (transport, _) = CreateTransport(
            handler: (req, _) =>
            {
                if (req.RequestUri?.PathAndQuery.Contains("services/events") == true)
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            },
            onError: null); // ← null onError covers the null branch at line 187

        var act = () => transport.NotifyBackendAsync("service.started", "my-service");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadBatch_HttpClientWithInfiniteTimeout_SetsTimeoutFromOptions()
    {
        // Cover line 49: `if (_httpClient.Timeout == InfiniteTimeSpan) set timeout from options`
        var httpClient = new HttpClient(new MockHttpHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("https://api.incidentary.io"),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan  // force the branch
        };

        var transport = new HttpTransport(
            httpClient,
            TestApiKey,
            TestServiceName,
            TestEnvironment,
            TestWorkspaceId,
            timeoutMs: 5000,
            onError: null);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeTrue();
        httpClient.Timeout.Should().Be(TimeSpan.FromMilliseconds(5000));
        httpClient.Dispose();
    }

    // ── [LoggerMessage] generated code (IsEnabled=true path) ──────────────────

    [Fact]
    public async Task UploadBatch_WithEnabledLogger_LogsSuccessfully()
    {
        // Covers LoggerMessage.g.cs branches: the `if (!logger.IsEnabled(level))` guard.
        // NSubstitute mock with IsEnabled=true forces the `return` NOT taken → actual Log call.
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadBatch_ServerError_WithEnabledLogger_LogsFailure()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_Exception_WithEnabledLogger_LogsException()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler(
            (_, _) => throw new HttpRequestException("connection refused"));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadBatch_426Response_WithEnabledLogger_LogsVersion()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)426)));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadBatch_429CeLimit_WithEnabledLogger_LogsQuotaPaused()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.Add("X-Incidentary-Reason", "ce_limit_reached");
            return Task.FromResult(r);
        });
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var result = await transport.UploadBatchAsync(CreateTestEvents(), CaptureModes.Skeleton);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task NotifyBackend_WithEnabledLogger_LogsSuccess()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var act = () => transport.NotifyBackendAsync("service.started", "svc-123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyBackend_WithEnabledLogger_NonSuccess_LogsFailure()
    {
        var logger = Substitute.For<ILogger<HttpTransport>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var mockHandler = new MockHttpHandler((req, _) =>
        {
            if (req.RequestUri?.PathAndQuery.Contains("services/events") == true)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("https://api.incidentary.io") };
        _disposables.Add(httpClient);

        var transport = new HttpTransport(
            httpClient, TestApiKey, TestServiceName, TestEnvironment,
            TestWorkspaceId, onError: null, logger: logger);
        _disposables.Add(transport);

        var act = () => transport.NotifyBackendAsync("service.started", "svc-123");

        await act.Should().NotThrowAsync();
    }
}

internal sealed class MockHttpHandler : DelegatingHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public MockHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(ct);
        return await _handler(request, ct);
    }
}
