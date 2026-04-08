using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Buffering;

/// <summary>
/// Retry-aware flush queue that batches events and sends them to the backend.
/// Failed batches are retried with exponential backoff before being dropped.
/// </summary>
internal sealed class FlushQueue : IAsyncDisposable
{
    private static readonly int[] RetryDelaysMs = [1_000, 4_000, 16_000];

    private readonly ITransport _transport;
    private readonly Action<Exception>? _onError;
    private readonly int _maxBatchSize;
    private readonly Func<int, CancellationToken, Task> _delayFunc;
    private readonly CancellationTokenSource _cts = new();

    private long _droppedCount;
    private long _totalFlushed;

    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public long TotalFlushed => Interlocked.Read(ref _totalFlushed);

    public FlushQueue(
        ITransport transport,
        Action<Exception>? onError = null,
        int maxBatchSize = 500,
        Func<int, CancellationToken, Task>? delayFunc = null)
    {
        _transport = transport;
        _onError = onError;
        _maxBatchSize = maxBatchSize;
        _delayFunc = delayFunc ?? Task.Delay;
    }

    /// <summary>
    /// Flushes a list of events to the transport, splitting into batches
    /// of <see cref="_maxBatchSize"/> and retrying failed batches with
    /// exponential backoff.
    /// </summary>
    public async Task FlushAsync(
        IReadOnlyList<CausalEvent> events,
        string captureMode,
        string? incidentId = null,
        CancellationToken ct = default)
    {
        if (events.Count == 0)
            return;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            var token = linkedCts.Token;

            for (var offset = 0; offset < events.Count; offset += _maxBatchSize)
            {
                if (token.IsCancellationRequested)
                    return;

                var batchSize = Math.Min(_maxBatchSize, events.Count - offset);
                var batch = events.Skip(offset).Take(batchSize).ToList();

                await SendBatchWithRetryAsync(batch, captureMode, incidentId, token);
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }

    private async Task SendBatchWithRetryAsync(
        List<CausalEvent> batch,
        string captureMode,
        string? incidentId,
        CancellationToken ct)
    {
        // 1 initial attempt + 3 retries = 4 total. Delays: [1s, 4s, 16s] between
        // attempts 0→1, 1→2, 2→3. The final (4th) attempt runs immediately after the 3rd delay.
        const int maxAttempts = 4;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (ct.IsCancellationRequested)
                return;

            try
            {
                var success = await _transport.UploadBatchAsync(batch, captureMode, incidentId, ct);

                if (success)
                {
                    Interlocked.Add(ref _totalFlushed, batch.Count);
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _onError?.Invoke(ex);

                // On last attempt, fall through to drop logic below
                if (attempt == maxAttempts - 1)
                    break;
            }

            // Wait before retrying (unless this was the last attempt)
            if (attempt < RetryDelaysMs.Length)
            {
                try
                {
                    await _delayFunc(RetryDelaysMs[attempt], ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        // All retries exhausted — drop the batch
        Interlocked.Add(ref _droppedCount, batch.Count);
        _onError?.Invoke(new InvalidOperationException(
            $"Batch of {batch.Count} events dropped: all retries exhausted."));
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
