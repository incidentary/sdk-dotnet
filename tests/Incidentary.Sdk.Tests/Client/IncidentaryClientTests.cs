using FluentAssertions;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

public sealed class IncidentaryClientTests : IDisposable
{
    private readonly ITransport _transport = Substitute.For<ITransport>();

    private readonly IncidentaryClientOptions _options = new()
    {
        ApiKey = "test-key",
        ServiceName = "test-service",
        BaseUrl = "https://api.test.io"
    };

    public IncidentaryClientTests()
    {
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true });

        _transport.IsHealthy.Returns(true);
    }

    public void Dispose()
    {
        // Tests that create clients dispose them via using statements.
    }

    private IncidentaryClient CreateClient() => new(_options, _transport);

    private static CausalEvent CreateEvent(string? eventType = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        TraceId = Guid.NewGuid().ToString(),
        ServiceId = "test-service",
        OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
        Kind = CeKind.HttpIn,
        Type = eventType ?? EventTypes.HttpServer,
        StatusCode = 200,
        DurationNs = 1_000_000,
    };

    // ── 1. InitialMode_IsNormal ────────────────────────────────────────

    [Fact]
    public void InitialMode_IsNormal()
    {
        using var client = CreateClient();

        client.CaptureMode.Should().Be(ClientCaptureMode.Normal);
    }

    // ── 2. RecordRequest_WritesEventToBuffer ────────────────────────────

    [Fact]
    public async Task RecordRequest_WritesEventToBuffer()
    {
        using var client = CreateClient();

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e => e.Count == 1),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 3. RecordRequest_NeverThrows ────────────────────────────────────

    [Fact]
    public void RecordRequest_NeverThrows()
    {
        using var client = CreateClient();

        var act = () => client.RecordRequest(200, null);

        act.Should().NotThrow();
    }

    // ── 4. RecordEvent_WithEventType_CreatesCorrectEvent ────────────────

    [Fact]
    public async Task RecordEvent_WithEventType_CreatesCorrectEvent()
    {
        using var client = CreateClient();

        client.RecordEvent("custom_event");
        await client.FlushToBackendAsync();

        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e =>
                e.Count == 1 && e[0].Type == "custom_event"),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 5. RecordQueuePublish_UsesCorrectKindAndType ────────────────────

    [Fact]
    public async Task RecordQueuePublish_UsesCorrectKindAndType()
    {
        using var client = CreateClient();

        client.RecordQueuePublish();
        await client.FlushToBackendAsync();

        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e =>
                e.Count == 1
                && e[0].Kind == CeKind.QueuePublish
                && e[0].Type == EventTypes.QueuePublish),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 6. RecordJobStart_UsesCorrectType ───────────────────────────────

    [Fact]
    public async Task RecordJobStart_UsesCorrectType()
    {
        using var client = CreateClient();

        client.RecordJobStart();
        await client.FlushToBackendAsync();

        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e =>
                e.Count == 1 && e[0].Type == "job_start"),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 7. WriteEvent_CanFlush ──────────────────────────────────────────

    [Fact]
    public async Task WriteEvent_CanFlush()
    {
        using var client = CreateClient();

        client.WriteEvent(CreateEvent());
        await client.FlushToBackendAsync();

        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e => e.Count == 1),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 8. FlushToBackendAsync_EmptyBuffer_NoTransportCall ──────────────

    [Fact]
    public async Task FlushToBackendAsync_EmptyBuffer_NoTransportCall()
    {
        using var client = CreateClient();

        await client.FlushToBackendAsync();

        await _transport.DidNotReceive().UploadBatchAsync(
            Arg.Any<IReadOnlyList<CausalEvent>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── 9. EscalateToIncident_ChangesMode ───────────────────────────────

    [Fact]
    public void EscalateToIncident_ChangesMode()
    {
        using var client = CreateClient();

        client.EscalateToIncident("inc-123");

        client.CaptureMode.Should().Be(ClientCaptureMode.Incident);
    }

    // ── 10. CloseIncident_ReturnsToNormal ───────────────────────────────

    [Fact]
    public void CloseIncident_ReturnsToNormal()
    {
        using var client = CreateClient();

        client.EscalateToIncident("inc-123");
        client.CloseIncident();

        client.CaptureMode.Should().Be(ClientCaptureMode.Normal);
    }

    // ── 11. ShouldCaptureDetail_FalseInNormal ───────────────────────────

    [Fact]
    public void ShouldCaptureDetail_FalseInNormal()
    {
        using var client = CreateClient();

        client.ShouldCaptureDetail.Should().BeFalse();
    }

    // ── 12. ShouldCaptureDetail_TrueInIncident ──────────────────────────

    [Fact]
    public void ShouldCaptureDetail_TrueInIncident()
    {
        using var client = CreateClient();

        client.EscalateToIncident("inc-456");

        client.ShouldCaptureDetail.Should().BeTrue();
    }

    // ── 13. Dispose_DoesNotThrow ────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var client = CreateClient();

        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    // ── 14. AllVocabularyHelpers_DoNotThrow ──────────────────────────────

    [Fact]
    public void AllVocabularyHelpers_DoNotThrow()
    {
        using var client = CreateClient();

        var act = () =>
        {
            client.RecordQueueConsume();
            client.RecordJobEnd();
            client.RecordWebhookIn();
            client.RecordWebhookOut();
        };

        act.Should().NotThrow();
    }

    // ── 15. FlushToBackend_LogsWhenCaptureModeRequested ────────────────

    [Fact]
    public async Task FlushToBackend_WhenCaptureModeRequested_Logs()
    {
        var logger = Substitute.For<ILogger<IncidentaryClient>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true, RequestedCaptureMode = "FULL" });

        using var client = new IncidentaryClient(_options, _transport, logger);

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("FULL")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ── 16. FlushToBackend_NoCaptureModeHeader_DoesNotLog ──────────────

    [Fact]
    public async Task FlushToBackend_NoCaptureModeRequested_DoesNotLogCaptureMode()
    {
        var logger = Substitute.For<ILogger<IncidentaryClient>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true });

        using var client = new IncidentaryClient(_options, _transport, logger);

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("requested capture mode")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
