using FluentAssertions;
using Incidentary.Sdk.Buffering;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Incidentary.Sdk.Tests.Buffering;

public sealed class FlushQueueTests : IAsyncDisposable
{
    private readonly ITransport _transport = Substitute.For<ITransport>();
    private readonly Func<int, CancellationToken, Task> _noDelay = (_, _) => Task.CompletedTask;
    private readonly List<Exception> _capturedErrors = [];

    private FlushQueue CreateQueue(int maxBatchSize = 500) =>
        new(
            _transport,
            onError: ex => _capturedErrors.Add(ex),
            maxBatchSize: maxBatchSize,
            delayFunc: _noDelay);

    private static IReadOnlyList<CausalEvent> CreateEvents(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new CausalEvent
            {
                Id = $"ce-{i}",
                TraceId = $"trace-{i}",
                ServiceId = "test-service",
                OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                Kind = CeKind.HttpIn,
                StatusCode = 200,
                DurationNs = 1_000_000,
            })
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose any queues created in tests? They manage their own CTS.
        // Individual tests dispose their own queue instances.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FlushAsync_Success_CallsTransport()
    {
        // Arrange
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true });

        await using var queue = CreateQueue();
        var events = CreateEvents(10);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert
        await _transport.Received(1).UploadBatchAsync(
            Arg.Is<IReadOnlyList<CausalEvent>>(e => e.Count == 10),
            "always",
            null,
            Arg.Any<CancellationToken>());

        queue.TotalFlushed.Should().Be(10);
    }

    [Fact]
    public async Task FlushAsync_TransportFails_RetriesTotalOf4()
    {
        // Arrange — transport always fails
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed);

        await using var queue = CreateQueue();
        var events = CreateEvents(5);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert — 1 initial + 3 retries = 4 total
        await _transport.Received(4).UploadBatchAsync(
            Arg.Any<IReadOnlyList<CausalEvent>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushAsync_TransportFailsThenSucceeds_StopsRetrying()
    {
        // Arrange — fails twice, succeeds on 3rd
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed, FlushResult.Failed, new FlushResult { Success = true });

        await using var queue = CreateQueue();
        var events = CreateEvents(5);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert — called 3 times total (2 failures + 1 success)
        await _transport.Received(3).UploadBatchAsync(
            Arg.Any<IReadOnlyList<CausalEvent>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        queue.TotalFlushed.Should().Be(5);
        queue.DroppedCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_AllRetriesExhausted_IncrementsDroppingCount()
    {
        // Arrange — transport always fails
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed);

        await using var queue = CreateQueue();
        var events = CreateEvents(7);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert
        queue.DroppedCount.Should().Be(7);
        queue.TotalFlushed.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_AllRetriesExhausted_CallsOnError()
    {
        // Arrange — transport always fails
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed);

        await using var queue = CreateQueue();
        var events = CreateEvents(3);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert
        _capturedErrors.Should().HaveCount(1);
        _capturedErrors[0].Message.Should().Contain("retries exhausted");
    }

    [Fact]
    public async Task FlushAsync_LargeBatch_SplitsIntoBatches()
    {
        // Arrange
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true });

        await using var queue = CreateQueue(maxBatchSize: 500);
        var events = CreateEvents(1200);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert — 500 + 500 + 200 = 3 batches
        await _transport.Received(3).UploadBatchAsync(
            Arg.Any<IReadOnlyList<CausalEvent>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        queue.TotalFlushed.Should().Be(1200);
    }

    [Fact]
    public async Task FlushAsync_EmptyEvents_DoesNotCallTransport()
    {
        // Arrange
        await using var queue = CreateQueue();
        var events = CreateEvents(0);

        // Act
        await queue.FlushAsync(events, "always");

        // Assert
        await _transport.DidNotReceive().UploadBatchAsync(
            Arg.Any<IReadOnlyList<CausalEvent>>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        queue.TotalFlushed.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_NeverThrows()
    {
        // Arrange — transport throws exception
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Boom"));

        await using var queue = CreateQueue();
        var events = CreateEvents(5);

        // Act — should not throw
        var act = () => queue.FlushAsync(events, "always");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TotalFlushed_TracksSuccessfulEvents()
    {
        // Arrange
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new FlushResult { Success = true });

        await using var queue = CreateQueue();

        // Act — flush two separate batches
        await queue.FlushAsync(CreateEvents(10), "always");
        await queue.FlushAsync(CreateEvents(20), "always");

        // Assert
        queue.TotalFlushed.Should().Be(30);
    }

    [Fact]
    public async Task FlushAsync_CancellationRequested_StopsEarly()
    {
        // Arrange — transport always fails (would retry indefinitely without cancellation)
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed);

        await using var queue = CreateQueue(maxBatchSize: 500);
        var events = CreateEvents(5);
        using var cts = new CancellationTokenSource();

        // Cancel immediately
        await cts.CancelAsync();

        // Act
        await queue.FlushAsync(events, "always", ct: cts.Token);

        // Assert — cancellation is checked before the first batch dispatch (FlushQueue line 59),
        // so a pre-cancelled token guarantees 0 transport calls.
        var callCount = _transport.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ITransport.UploadBatchAsync));

        callCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_TransportThrowsOce_WhenCancelled_StopsRetrying()
    {
        // Exercise the `catch (OperationCanceledException) when (ct.IsCancellationRequested)`
        // path inside SendBatchWithRetryAsync (line ~99 in FlushQueue.cs).
        // The transport cancels the source token and then throws OCE; the retry loop
        // must catch it and return without propagating.
        using var cts = new CancellationTokenSource();

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<FlushResult>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException("transport cancelled", cts.Token);
            });

        await using var queue = CreateQueue();

        // Should complete without throwing
        var act = () => queue.FlushAsync(CreateEvents(3), "always", ct: cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendBatchWithRetry_CtCancelledDuringException_StopsAtNextAttemptCheck()
    {
        // Exercise the CT-cancelled early-return at the TOP of each retry iteration (line 86-87).
        // The transport cancels the CT while throwing a non-OCE exception.
        // The delay runs (no-op), then the NEXT iteration's CT check triggers the early return.
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<FlushResult>(_ =>
            {
                callCount++;
                cts.Cancel(); // cancel CT during attempt 0 exception handling
                throw new InvalidOperationException("transport failed");
            });

        // _noDelay is used inside CreateQueue, so the delay does NOT check the CT.
        // After attempt 0 throws, delay runs (no-op), then attempt 1 sees ct.IsCancellationRequested = true → return.
        await using var queue = CreateQueue();

        await queue.FlushAsync(CreateEvents(3), "always", ct: cts.Token);

        // Only one transport call should have been made (attempt 0)
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task SendBatchWithRetry_AllRetriesExhausted_NullOnError_DoesNotThrow()
    {
        // Cover the null branch of `_onError?.Invoke(...)` at retry exhaustion (line 128).
        // FlushQueue with NO onError callback: all retries fail → silently drops.
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed); // always fails → exhausts all retries

        var queue = new FlushQueue(
            _transport,
            onError: null,   // ← null onError: covers the null branch at line 128
            delayFunc: _noDelay);
        await using var _ = queue;

        var act = () => queue.FlushAsync(CreateEvents(5), "always");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_CancellationDuringRetryDelay_StopsRetrying()
    {
        // Exercise `catch (OperationCanceledException) when (ct.IsCancellationRequested)` in
        // the retry delay block (line ~119 in FlushQueue.cs).
        // Transport fails (false), then the delay function cancels the source token and
        // throws OCE — the outer loop must catch it and return cleanly.
        using var cts = new CancellationTokenSource();

        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(FlushResult.Failed); // always fails → retry delay will be attempted

        Func<int, CancellationToken, Task> cancellingDelay = (_, ct) =>
        {
            // Cancel the parent token so ct.IsCancellationRequested is true,
            // then throw OCE so the `when` clause is satisfied.
            cts.Cancel();
            return Task.FromCanceled(ct);
        };

        var queue = new FlushQueue(
            _transport,
            onError: ex => _capturedErrors.Add(ex),
            delayFunc: cancellingDelay);
        await using var _ = queue;

        var act = () => queue.FlushAsync(CreateEvents(3), "always", ct: cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_AfterDispose_NullOnError_DoesNotThrow()
    {
        // Cover line 70 null branch: outer FlushAsync catch fires (ObjectDisposedException
        // from _cts.Token access after dispose) with _onError == null.
        var queue = new FlushQueue(_transport, onError: null, delayFunc: _noDelay);
        await queue.DisposeAsync(); // disposes _cts

        // After dispose, _cts.Token throws → outer catch at line 68 fires → line 70 null branch
        var act = () => queue.FlushAsync(CreateEvents(3), "always");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_AfterDispose_NonNullOnError_CallsOnError()
    {
        // Cover line 70 non-null branch: outer FlushAsync catch fires with _onError set.
        var errors = new List<Exception>();
        var queue = new FlushQueue(_transport, onError: ex => errors.Add(ex), delayFunc: _noDelay);
        await queue.DisposeAsync();

        await queue.FlushAsync(CreateEvents(3), "always");

        errors.Should().HaveCount(1);
        errors[0].Should().BeOfType<ObjectDisposedException>();
    }

    [Fact]
    public async Task SendBatchWithRetry_TransportThrows_NullOnError_DoesNotThrow()
    {
        // Cover line 105 null branch: transport throws exception, _onError is null.
        // The catch at line 103 fires → line 105 _onError?.Invoke(ex) with null → null branch.
        _transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("transport boom"));

        var queue = new FlushQueue(_transport, onError: null, delayFunc: _noDelay);
        await using var _ = queue;

        var act = () => queue.FlushAsync(CreateEvents(3), "always");

        await act.Should().NotThrowAsync();
    }
}
