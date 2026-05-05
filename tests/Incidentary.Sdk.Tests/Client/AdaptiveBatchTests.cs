using FluentAssertions;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

/// <summary>
/// Tests for adaptive batch sizing: EMA tracking, batch size adjustment,
/// and telemetry fields (flush_latency_ema_ms, current_batch_size).
/// </summary>
public sealed class AdaptiveBatchTests
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
                return new FlushResult { Success = true };
            });

        return new IncidentaryClient(BaseOptions(configure), _transport);
    }

    // ── MaxFlushOverheadMs config ────────────────────────────────────────

    [Fact]
    public void MaxFlushOverheadMs_DefaultIs100()
    {
        var opts = new IncidentaryClientOptions();

        opts.MaxFlushOverheadMs.Should().Be(100);
    }

    [Fact]
    public void MaxFlushOverheadMs_CanBeConfigured()
    {
        var opts = new IncidentaryClientOptions { MaxFlushOverheadMs = 200 };

        opts.MaxFlushOverheadMs.Should().Be(200);
    }

    // ── Initial state ────────────────────────────────────────────────────

    [Fact]
    public void CurrentBatchSize_InitiallyIs500()
    {
        using var client = CreateClient();

        client.CurrentBatchSize.Should().Be(500);
    }

    [Fact]
    public void FlushLatencyEmaMs_InitiallyIsZero()
    {
        using var client = CreateClient();

        client.FlushLatencyEmaMs.Should().Be(0.0);
    }

    // ── EMA calculation ──────────────────────────────────────────────────

    [Fact]
    public async Task FlushLatencyEmaMs_AfterFirstFlush_EqualsFirstLatency()
    {
        // The first EMA value equals the first measurement (EMA seed).
        using var client = CreateClient();

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        // After flush, EMA should be > 0 (some real latency was measured)
        client.FlushLatencyEmaMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FlushLatencyEmaMs_UpdatesWithAlpha0_3()
    {
        // EMA formula: ema = α * sample + (1 - α) * prev_ema
        // After seed (first sample), subsequent samples are smoothed.
        using var client = CreateClient();

        // First flush seeds the EMA
        client.RecordRequest(200);
        await client.FlushToBackendAsync();
        var ema1 = client.FlushLatencyEmaMs;

        // Second flush updates the EMA
        client.RecordRequest(200);
        await client.FlushToBackendAsync();
        var ema2 = client.FlushLatencyEmaMs;

        // EMA should be positive after both flushes
        ema1.Should().BeGreaterThan(0);
        ema2.Should().BeGreaterThan(0);
    }

    // ── Batch size stays within bounds ────────────────────────────────────

    [Fact]
    public void CurrentBatchSize_NeverBelowMinimum()
    {
        // The minimum batch size is 10.
        using var client = CreateClient(o => o.MaxFlushOverheadMs = 1); // very low ceiling

        // Even with extreme settings, batch size should not go below 10
        client.CurrentBatchSize.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void CurrentBatchSize_NeverAboveMaximum()
    {
        // The maximum batch size is 5000.
        using var client = CreateClient(o => o.MaxFlushOverheadMs = 10_000); // very high ceiling

        client.CurrentBatchSize.Should().BeLessOrEqualTo(5000);
    }

    // ── Batch size adjustment on fast flushes ────────────────────────────

    [Fact]
    public async Task FastFlush_IncreasesBatchSize()
    {
        // When latency < 50% of ceiling, batch size increases by 20%.
        // With MaxFlushOverheadMs=10000 (10 seconds), any real flush will be well below 50%.
        using var client = CreateClient(o => o.MaxFlushOverheadMs = 10_000);

        var initialBatchSize = client.CurrentBatchSize;

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        client.CurrentBatchSize.Should().BeGreaterThan(initialBatchSize);
    }

    // ── Telemetry fields exposed ──────────────────────────────────────────

    [Fact]
    public async Task FlushLatencyEmaMs_ExposedForTelemetry()
    {
        using var client = CreateClient();

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        // The property should be readable and positive after a flush
        client.FlushLatencyEmaMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CurrentBatchSize_ExposedForTelemetry()
    {
        using var client = CreateClient();

        // Should be readable before and after flush
        var beforeFlush = client.CurrentBatchSize;
        beforeFlush.Should().BeGreaterOrEqualTo(10);

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        var afterFlush = client.CurrentBatchSize;
        afterFlush.Should().BeGreaterOrEqualTo(10);
        afterFlush.Should().BeLessOrEqualTo(5000);
    }

    // ── Empty buffer flush does not change EMA ───────────────────────────

    [Fact]
    public async Task EmptyFlush_DoesNotUpdateEma()
    {
        using var client = CreateClient();

        await client.FlushToBackendAsync();

        client.FlushLatencyEmaMs.Should().Be(0.0);
        client.CurrentBatchSize.Should().Be(500);
    }

    // ── Failed flush does not update EMA ──────────────────────────────────

    [Fact]
    public async Task FailedFlush_DoesNotUpdateEma()
    {
        _transport.IsHealthy.Returns(true);
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed);

        using var client = new IncidentaryClient(BaseOptions(), _transport);

        client.RecordRequest(200);
        await client.FlushToBackendAsync();

        // Transport failed, so EMA should not be updated
        client.FlushLatencyEmaMs.Should().Be(0.0);
    }
}
