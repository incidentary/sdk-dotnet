using FluentAssertions;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

/// <summary>
/// Advanced tests covering detail capture, event annotations, error callbacks,
/// event options propagation, and lifecycle behavior.
/// </summary>
public sealed class IncidentaryClientAdvancedTests
{
    private readonly ITransport _transport = Substitute.For<ITransport>();
    private List<IReadOnlyList<CausalEvent>>? _capturedBatches;

    private static IncidentaryClientOptions BaseOptions(Action<IncidentaryClientOptions>? configure = null)
    {
        var opts = new IncidentaryClientOptions
        {
            ApiKey = "test-key",
            ServiceName = "test-service",
            BaseUrl = "https://api.test.io"
        };
        configure?.Invoke(opts);
        return opts;
    }

    private IncidentaryClient CreateClient(Action<IncidentaryClientOptions>? configure = null)
    {
        _capturedBatches = [];
        _transport.IsHealthy.Returns(true);
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _capturedBatches!.Add(callInfo.Arg<IReadOnlyList<CausalEvent>>());
                return true;
            });

        return new IncidentaryClient(BaseOptions(configure), _transport);
    }

    // ── ShouldCaptureDetail ────────────────────────────────────────────────

    [Fact]
    public void ShouldCaptureDetail_DetailDisabled_AlwaysFalse_EvenInIncident()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = false);

        client.EscalateToIncident("inc-1");

        client.ShouldCaptureDetail.Should().BeFalse();
    }

    [Fact]
    public void ShouldCaptureDetail_DetailEnabled_NormalMode_IsFalse()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);

        // Mode is Normal by default
        client.ShouldCaptureDetail.Should().BeFalse();
    }

    [Fact]
    public void ShouldCaptureDetail_DetailEnabled_IncidentMode_IsTrue()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);

        client.EscalateToIncident("inc-1");

        client.ShouldCaptureDetail.Should().BeTrue();
    }

    // ── RecordRequest with options ─────────────────────────────────────────

    [Fact]
    public async Task RecordRequest_WithExplicitTraceId_UsesProvidedTraceId()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { TraceId = "explicit-trace" });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().TraceId.Should().Be("explicit-trace");
    }

    [Fact]
    public async Task RecordRequest_WithExplicitParentCeId_PropagatesParent()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { ParentCeId = "parent-ce-123" });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().ParentCeId.Should().Be("parent-ce-123");
    }

    [Fact]
    public async Task RecordRequest_WithDurationNs_PropagatesDuration()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { DurationNs = 123_456_789 });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().DurationNs.Should().Be(123_456_789);
    }

    [Fact]
    public async Task RecordRequest_WithEventAttrs_PropagatesAttrs()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions
        {
            EventAttrs = new Dictionary<string, object> { ["custom_key"] = "custom_value" }
        });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().EventAttrs.Should().ContainKey("custom_key")
            .WhoseValue.Should().Be("custom_value");
    }

    [Fact]
    public async Task RecordRequest_WithCustomEventType_UsesOverriddenType()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { EventType = "webhook_in" });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().EventType.Should().Be("webhook_in");
    }

    [Fact]
    public async Task RecordRequest_HttpOutKind_UsesCorrectKindAndType()
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { Kind = CeKind.HttpOut });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var ce = batch.Single();
        ce.Kind.Should().Be(CeKind.HttpOut);
        ce.EventType.Should().Be(EventTypes.HttpOut);
    }

    [Fact]
    public async Task RecordRequest_ServiceId_MatchesOptionsServiceName()
    {
        using var client = CreateClient(o => o.ServiceName = "my-payments-service");

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().ServiceId.Should().Be("my-payments-service");
    }

    [Fact]
    public async Task RecordRequest_CeId_IsValidGuid()
    {
        using var client = CreateClient();

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        Guid.TryParse(batch.Single().CeId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task RecordRequest_WallTsNs_IsRecentTimestamp()
    {
        var beforeNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;

        using var client = CreateClient();
        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var afterNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;

        var batch = _capturedBatches!.Single();
        batch.Single().WallTsNs.Should().BeInRange(beforeNs, afterNs);
    }

    [Fact]
    public async Task RecordRequest_Detail_NullInNormalMode()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);
        // Stay in Normal mode

        client.RecordRequest(200, new RecordRequestOptions { Method = "GET", RouteTemplate = "/users" });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().Detail.Should().BeNull();
    }

    [Fact]
    public async Task RecordRequest_Detail_PopulatedInIncidentMode()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);

        client.EscalateToIncident("inc-1");
        client.RecordRequest(200, new RecordRequestOptions
        {
            Method = "POST",
            RouteTemplate = "/orders/{id}",
            RequestBytes = 512,
            ResponseBytes = 1024
        });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var detail = batch.Single().Detail;
        detail.Should().NotBeNull();
        detail!.Method.Should().Be("POST");
        detail.RouteTemplate.Should().Be("/orders/{id}");
        detail.RequestBytes.Should().Be(512);
        detail.ResponseBytes.Should().Be(1024);
    }

    [Fact]
    public async Task RecordRequest_Cancelled_SetsLocalErrorClassification()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);
        client.EscalateToIncident("inc-1");

        client.RecordRequest(0, new RecordRequestOptions { Cancelled = true });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().Detail?.LocalErrorClassification.Should().Be("cancelled");
    }

    [Fact]
    public async Task RecordRequest_TimedOut_SetsLocalErrorClassification()
    {
        using var client = CreateClient(o => o.DetailCaptureEnabled = true);
        client.EscalateToIncident("inc-1");

        client.RecordRequest(0, new RecordRequestOptions { TimedOut = true });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().Detail?.LocalErrorClassification.Should().Be("timeout");
    }

    // ── RecordEvent ────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordEvent_WithTopic_InjectsTopicIntoAttrs()
    {
        using var client = CreateClient();

        client.RecordEvent("queue_publish", new RecordEventOptions { Topic = "orders.created" });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().EventAttrs.Should().ContainKey("topic")
            .WhoseValue.Should().Be("orders.created");
    }

    [Fact]
    public async Task RecordEvent_WithCustomAttrsAndTopic_MergesCorrectly()
    {
        using var client = CreateClient();

        client.RecordEvent("db_query", new RecordEventOptions
        {
            Topic = "orders",
            EventAttrs = new Dictionary<string, object> { ["db_name"] = "postgres" }
        });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var attrs = batch.Single().EventAttrs;
        attrs.Should().ContainKey("topic").WhoseValue.Should().Be("orders");
        attrs.Should().ContainKey("db_name").WhoseValue.Should().Be("postgres");
    }

    [Fact]
    public async Task RecordEvent_WithDurationAndStatus_Propagates()
    {
        using var client = CreateClient();

        client.RecordEvent("job_start", new RecordEventOptions
        {
            Status = 500,
            DurationNs = 99_000_000
        });
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var ce = batch.Single();
        ce.Status.Should().Be(500);
        ce.DurationNs.Should().Be(99_000_000);
    }

    [Fact]
    public async Task RecordEvent_DefaultStatus_Is200()
    {
        using var client = CreateClient();

        client.RecordEvent("internal_task");
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().Status.Should().Be(200);
    }

    [Fact]
    public async Task RecordEvent_EventClass_IsCausal()
    {
        using var client = CreateClient();

        client.RecordEvent("custom_event");
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().EventClass.Should().Be("causal");
    }

    // ── Vocabulary helpers ─────────────────────────────────────────────────

    [Fact]
    public async Task RecordWebhookIn_UsesCorrectTypeAndKind()
    {
        using var client = CreateClient();

        client.RecordWebhookIn();
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var ce = batch.Single();
        ce.EventType.Should().Be("webhook_in");
        ce.Kind.Should().Be(CeKind.HttpIn);
    }

    [Fact]
    public async Task RecordWebhookOut_UsesCorrectTypeAndKind()
    {
        using var client = CreateClient();

        client.RecordWebhookOut();
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var ce = batch.Single();
        ce.EventType.Should().Be("webhook_out");
        ce.Kind.Should().Be(CeKind.HttpOut);
    }

    [Fact]
    public async Task RecordQueueConsume_UsesCorrectTypeAndKind()
    {
        using var client = CreateClient();

        client.RecordQueueConsume();
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        var ce = batch.Single();
        ce.EventType.Should().Be(EventTypes.QueueConsume);
        ce.Kind.Should().Be(CeKind.QueueConsume);
    }

    [Fact]
    public async Task RecordJobEnd_UsesCorrectType()
    {
        using var client = CreateClient();

        client.RecordJobEnd();
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().EventType.Should().Be("job_end");
    }

    // ── FlushToBackendAsync annotations ───────────────────────────────────

    [Fact]
    public async Task Flush_NormalMode_SetsCapturedBeforeAlertTrue()
    {
        using var client = CreateClient();

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().CapturedBeforeAlert.Should().BeTrue();
    }

    [Fact]
    public async Task Flush_IncidentMode_SetsCapturedBeforeAlertFalse()
    {
        using var client = CreateClient();

        client.EscalateToIncident("inc-1");
        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Single().CapturedBeforeAlert.Should().BeFalse();
    }

    [Fact]
    public async Task Flush_AnnotatesRingBufferSeqSequentially()
    {
        using var client = CreateClient();

        client.RecordRequest(200);
        client.RecordRequest(200);
        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Should().HaveCount(3);
        batch[0].RingBufferSeq.Should().Be(0);
        batch[1].RingBufferSeq.Should().Be(1);
        batch[2].RingBufferSeq.Should().Be(2);
    }

    [Fact]
    public async Task Flush_WithIncidentId_PassesIncidentIdToTransport()
    {
        using var client = CreateClient();
        string? capturedIncidentId = null;

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedIncidentId = callInfo.ArgAt<string?>(2);
                return true;
            });

        client.RecordRequest(200);
        await client.FlushToBackendAsync(incidentId: "my-incident-42");

        capturedIncidentId.Should().Be("my-incident-42");
    }

    [Fact]
    public async Task Flush_NormalMode_UsesSKELETONCaptureMode()
    {
        using var client = CreateClient();
        string? capturedMode = null;

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedMode = callInfo.ArgAt<string>(1);
                return true;
            });

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        capturedMode.Should().Be("SKELETON");
    }

    [Fact]
    public async Task Flush_IncidentMode_UsesFULLCaptureMode()
    {
        using var client = CreateClient();
        string? capturedMode = null;

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedMode = callInfo.ArgAt<string>(1);
                return true;
            });

        client.EscalateToIncident("inc-1");
        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        capturedMode.Should().Be("FULL");
    }

    // ── GetPreArmDebugState ────────────────────────────────────────────────

    [Fact]
    public void GetPreArmDebugState_ReturnsNonNull()
    {
        using var client = CreateClient();

        var state = client.GetPreArmDebugState();

        state.Should().NotBeNull();
    }

    [Fact]
    public void GetPreArmDebugState_InitialMode_IsNormal()
    {
        using var client = CreateClient();

        var state = client.GetPreArmDebugState();

        state.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void GetPreArmDebugState_AfterEscalation_IsIncident()
    {
        using var client = CreateClient();

        client.EscalateToIncident("inc-1");

        var state = client.GetPreArmDebugState();

        state.Mode.Should().Be(ClientCaptureMode.Incident);
    }

    // ── OnError callback ───────────────────────────────────────────────────

    [Fact]
    public async Task OnError_CalledWhenTransportFails_AfterRetries()
    {
        var errors = new List<Exception>();
        using var client = CreateClient(o => o.OnError = errors.Add);

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(false); // Always fail → retries exhausted

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        errors.Should().ContainSingle();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_IdempotentWhenCalledTwice()
    {
        var client = CreateClient();

        client.Dispose();

        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_FlushesBufferedEvents()
    {
        // Create the client first (CreateClient sets up the transport mock),
        // then override the transport behavior to track the dispose-time flush.
        var client = CreateClient();
        var flushed = false;

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                flushed = true;
                return true;
            });

        client.RecordRequest(200);

        // DisposeAsync must flush before releasing resources
        await client.DisposeAsync();

        flushed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_IdempotentWhenCalledTwice()
    {
        var client = CreateClient();

        await client.DisposeAsync();

        var act = () => client.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    // ── WriteEvent low-level ───────────────────────────────────────────────

    [Fact]
    public async Task WriteEvent_DirectlyWritten_IsIncludedInFlush()
    {
        using var client = CreateClient();

        var ce = new CausalEvent
        {
            CeId = "manual-ce-id",
            TraceId = "manual-trace-id",
            ServiceId = "test-service",
            WallTsNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
            Kind = CeKind.Internal,
            EventType = "internal_task",
            Status = 200,
            DurationNs = 0,
            SdkVersion = SdkVersion.Current
        };

        client.WriteEvent(ce);
        await client.FlushToBackendAsync();

        var batch = _capturedBatches!.Single();
        batch.Should().HaveCount(1);
        batch[0].CeId.Should().Be("manual-ce-id");
    }

    // ── RecordRequestStart ─────────────────────────────────────────────────

    [Fact]
    public void RecordRequestStart_NeverThrows()
    {
        using var client = CreateClient();

        var act = () => client.RecordRequestStart(CeKind.HttpIn);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRequestStart_WithHttpOutKind_NeverThrows()
    {
        using var client = CreateClient();

        var act = () => client.RecordRequestStart(CeKind.HttpOut);

        act.Should().NotThrow();
    }

    // ── Security: Header allowlist enforcement ─────────────────────────────

    [Fact]
    public async Task RecordRequest_SensitiveHeadersNotInAllowlist_AreStripped()
    {
        var client = CreateClient(o =>
        {
            o.DetailCaptureEnabled = true;
            // Default allowlist: content-type, content-length, user-agent, x-request-id, accept
        });
        client.EscalateToIncident("inc-1");

        client.RecordRequest(200, new RecordRequestOptions
        {
            RequestHeaders = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",  // in allowlist
                ["Authorization"] = "Bearer secret-token",  // NOT in allowlist — must be stripped
                ["Cookie"] = "session=abc123",  // NOT in allowlist — must be stripped
                ["x-request-id"] = "req-123"  // in allowlist
            }
        });

        await client.FlushToBackendAsync("inc-1");

        var events = _capturedBatches!.SelectMany(b => b).ToList();
        events.Should().ContainSingle();
        var headers = events[0].Detail!.RequestHeaders;
        headers.Should().NotBeNull();
        headers!.Should().ContainKey("content-type");
        headers.Should().ContainKey("x-request-id");
        headers.Should().NotContainKey("Authorization");
        headers.Should().NotContainKey("Cookie");
    }

    [Fact]
    public async Task RecordRequest_AllSensitiveHeaders_AllowlistEmpty_NullHeaders()
    {
        var client = CreateClient(o =>
        {
            o.DetailCaptureEnabled = true;
            o.RequestHeaderAllowlist = [];  // empty allowlist → no headers captured
        });
        client.EscalateToIncident("inc-1");

        client.RecordRequest(200, new RecordRequestOptions
        {
            RequestHeaders = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["Authorization"] = "Bearer secret"
            }
        });

        await client.FlushToBackendAsync("inc-1");

        var events = _capturedBatches!.SelectMany(b => b).ToList();
        events.Should().ContainSingle();
        // Empty allowlist → FilterHeaders returns null
        events[0].Detail!.RequestHeaders.Should().BeNull();
    }

    // ── MapKindToEventType branch coverage ────────────────────────────────────
    // RecordRequest uses MapKindToEventType when options?.Kind is not HttpIn.
    // These tests cover the QueuePublish, QueueConsume, and Internal switch arms.

    [Theory]
    [InlineData(CeKind.QueuePublish, EventTypes.QueuePublish)]
    [InlineData(CeKind.QueueConsume, EventTypes.QueueConsume)]
    [InlineData(CeKind.Internal, EventTypes.InternalTask)]
    [InlineData(CeKind.HttpOut, EventTypes.HttpOut)]
    public async Task RecordRequest_NonDefaultKind_MapsEventTypeCorrectly(CeKind kind, string expectedEventType)
    {
        using var client = CreateClient();

        client.RecordRequest(200, new RecordRequestOptions { Kind = kind });
        await client.FlushToBackendAsync();

        var events = _capturedBatches!.SelectMany(b => b).ToList();
        events.Should().ContainSingle();
        events[0].Kind.Should().Be(kind);
        events[0].EventType.Should().Be(expectedEventType);
    }

    // ── WithKind non-null options path ────────────────────────────────────────
    // RecordQueuePublish/RecordWebhookIn etc. call WithKind(options, kind).
    // When options is non-null, WithKind copies all fields to a new RecordEventOptions.

    [Fact]
    public async Task RecordQueuePublish_WithOptions_PropagatesOptionsFields()
    {
        using var client = CreateClient();

        var opts = new RecordEventOptions
        {
            Status = 202,
            DurationNs = 500_000,
            TraceId = "trace-abc",
            ParentCeId = "parent-xyz",
            Topic = "orders.created",
            EventAttrs = new Dictionary<string, object> { ["key"] = "val" }
        };

        client.RecordQueuePublish(opts);
        await client.FlushToBackendAsync();

        var events = _capturedBatches!.SelectMany(b => b).ToList();
        events.Should().ContainSingle();
        var ce = events[0];
        ce.Kind.Should().Be(CeKind.QueuePublish);
        ce.Status.Should().Be(202);
        ce.DurationNs.Should().Be(500_000);
        ce.TraceId.Should().Be("trace-abc");
        ce.ParentCeId.Should().Be("parent-xyz");
    }

    [Fact]
    public async Task RecordWebhookIn_WithOptions_PropagatesOptionsFields()
    {
        using var client = CreateClient();

        var opts = new RecordEventOptions
        {
            Status = 200,
            TraceId = "wh-trace"
        };

        client.RecordWebhookIn(opts);
        await client.FlushToBackendAsync();

        var events = _capturedBatches!.SelectMany(b => b).ToList();
        events.Should().ContainSingle();
        events[0].Kind.Should().Be(CeKind.HttpIn);
        events[0].TraceId.Should().Be("wh-trace");
    }

    // ── OnModeChanged with logger ─────────────────────────────────────────────
    // When a non-null logger is provided and the capture mode changes,
    // OnModeChanged calls LogModeChanged which is source-generated.

    [Fact]
    public void EscalateToIncident_WithLogger_LogsModeChange()
    {
        // NullLogger<T>.Instance absorbs the log call without throwing.
        var logger = NullLogger<IncidentaryClient>.Instance;
        using var client = new IncidentaryClient(BaseOptions(), _transport, logger);

        // Must not throw — mode change triggers OnModeChanged which calls LogModeChanged.
        var act = () => client.EscalateToIncident("inc-1");

        act.Should().NotThrow();
        client.CaptureMode.Should().Be(ClientCaptureMode.Incident);
    }

    [Fact]
    public void CloseIncident_WithLogger_LogsModeChange()
    {
        var logger = NullLogger<IncidentaryClient>.Instance;
        using var client = new IncidentaryClient(BaseOptions(), _transport, logger);

        client.EscalateToIncident("inc-1");
        var act = () => client.CloseIncident();

        act.Should().NotThrow();
        client.CaptureMode.Should().Be(ClientCaptureMode.Normal);
    }

    // ── DisposeAsync idempotency ──────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // DisposeAsync has `if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;`
        // The second call must hit the early-return path without double-disposing.
        var client = new IncidentaryClient(BaseOptions(), _transport);

        await client.DisposeAsync();
        var act = () => client.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    // ── Public constructor DisposeAsync (lines 347, 350) ─────────────────────

    [Fact]
    public async Task DisposeAsync_PublicConstructor_DisposesOwnedHttpClient()
    {
        // Uses the PUBLIC constructor, which creates its own HttpClient (_ownedHttpClient).
        // DisposeAsync at line 350: `_ownedHttpClient?.Dispose()` — covers the non-null branch.
        // Use a very short timeout so flush attempt doesn't block.
        var client = new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "test",
            ServiceName = "svc",
            TimeoutMs = 100,
        });

        var act = () => client.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithAutoInstrument_DisposesIntegrationRegistry()
    {
        // AutoInstrument = true creates _integrationRegistry.
        // DisposeAsync at line 347: `_integrationRegistry?.Dispose()` — covers the non-null branch.
        var client = new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "test",
            ServiceName = "svc",
            TimeoutMs = 100,
            AutoInstrument = true,
        });

        var act = () => client.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    // ── MapKindToEventType `_` arm (line 400) ────────────────────────────────

    [Fact]
    public async Task RecordRequest_UnknownKind_UsesInternalTaskEventType()
    {
        // (CeKind)999 falls through to the `_ => EventTypes.InternalTask` arm in the switch.
        using var client = CreateClient();

        client.RecordRequest(200, options: new RecordRequestOptions
        {
            Kind = (CeKind)999,
        });

        var events = await FlushAndCapture(client);
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be(EventTypes.InternalTask);
    }

    // ── IncidentaryActivity.Current propagation (lines 123-124, 176-177) ─────

    [Fact]
    public async Task RecordRequest_WithActivityContext_UsesContextTraceId()
    {
        // When options.TraceId is null, falls back to IncidentaryActivity.Current.TraceId.
        using var client = CreateClient();
        var ctx = new TraceContext("trace-from-context", "ce-from-context");

        using (IncidentaryActivity.SetContext(ctx))
        {
            client.RecordRequest(200, options: new RecordRequestOptions { TraceId = null });
        }

        var events = await FlushAndCapture(client);
        events.Should().HaveCount(1);
        events[0].TraceId.Should().Be("trace-from-context");
        events[0].ParentCeId.Should().Be("ce-from-context");
    }

    [Fact]
    public async Task RecordEvent_WithActivityContext_UsesContextTraceId()
    {
        // When options.TraceId is null, falls back to IncidentaryActivity.Current.TraceId.
        using var client = CreateClient();
        var ctx = new TraceContext("trace-event-ctx", "ce-event-ctx");

        using (IncidentaryActivity.SetContext(ctx))
        {
            client.RecordEvent("test.event", options: new RecordEventOptions { TraceId = null });
        }

        var events = await FlushAndCapture(client);
        events.Should().HaveCount(1);
        events[0].TraceId.Should().Be("trace-event-ctx");
        events[0].ParentCeId.Should().Be("ce-event-ctx");
    }

    // ── [LoggerMessage] generated code (IsEnabled=true path) ─────────────────

    [Fact]
    public void EscalateToIncident_WithEnabledLogger_LogsModeChange()
    {
        // LogModeChanged is called in OnModeChanged when _logger is not null.
        // The [LoggerMessage] generated code: if (!logger.IsEnabled(level)) return;
        // With a mock returning IsEnabled=true, the actual Log call executes (covers line 69 in g.cs).
        var logger = Substitute.For<ILogger<IncidentaryClient>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        using var client = new IncidentaryClient(BaseOptions(), _transport, logger);

        var act = () => client.EscalateToIncident("inc-log-1");
        act.Should().NotThrow();
    }

    [Fact]
    public void CloseIncident_WithEnabledLogger_LogsModeChange()
    {
        var logger = Substitute.For<ILogger<IncidentaryClient>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        using var client = new IncidentaryClient(BaseOptions(), _transport, logger);

        client.EscalateToIncident("inc-log-2");
        var act = () => client.CloseIncident();
        act.Should().NotThrow();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<CausalEvent>> FlushAndCapture(IncidentaryClient client)
    {
        await client.FlushToBackendAsync();
        await Task.Delay(50); // allow flush queue to process
        return _capturedBatches?.SelectMany(b => b).ToList() ?? [];
    }
}
