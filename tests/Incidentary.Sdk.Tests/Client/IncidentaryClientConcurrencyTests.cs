using FluentAssertions;
using Incidentary.Sdk.Transport;
using Incidentary.Sdk.WireFormat;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

/// <summary>
/// Thread-safety and concurrent-access tests for <see cref="IncidentaryClient"/>.
/// All public methods are expected to be safe to call concurrently without throwing.
/// </summary>
public sealed class IncidentaryClientConcurrencyTests
{
    private static IncidentaryClient CreateClient()
    {
        var transport = Substitute.For<ITransport>();
        transport.IsHealthy.Returns(true);
        transport.UploadBatchAsync(
                Arg.Any<IReadOnlyList<CausalEvent>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        return new IncidentaryClient(
            new IncidentaryClientOptions
            {
                ApiKey = "key",
                ServiceName = "service"
            },
            transport);
    }

    // ── Concurrent writes ─────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentRecordRequest_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                try { client.RecordRequest(200); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRecordEvent_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
            {
                try { client.RecordEvent($"event_{i}"); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentWriteAndFlush_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var writers = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    for (var i = 0; i < 20; i++)
                    {
                        client.RecordRequest(200);
                        await Task.Yield();
                    }
                }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }));

        var flushers = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    for (var i = 0; i < 10; i++)
                    {
                        await client.FlushToBackendAsync();
                        await Task.Yield();
                    }
                }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }));

        await Task.WhenAll(writers.Concat(flushers));

        exceptions.Should().BeEmpty();
    }

    // ── Concurrent mode transitions ────────────────────────────────────────

    [Fact]
    public async Task ConcurrentEscalateAndClose_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var escalators = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() =>
            {
                try { client.EscalateToIncident("inc-1"); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }));

        var closers = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() =>
            {
                try { client.CloseIncident(); }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }));

        await Task.WhenAll(escalators.Concat(closers));

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentAllOperations_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var tasks = new List<Task>
        {
            Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                    try { client.RecordRequest(200 + (i % 300)); }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                    try { client.RecordEvent("custom"); }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                    try { client.RecordQueuePublish(); }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                    try { client.RecordQueueConsume(); }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < 10; i++)
                {
                    try { await client.FlushToBackendAsync(); }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
                    await Task.Delay(1);
                }
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    try
                    {
                        client.EscalateToIncident("inc-concurrent");
                        client.CloseIncident();
                    }
                    catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
                }
            })
        };

        await Task.WhenAll(tasks);

        exceptions.Should().BeEmpty();
    }

    // ── CaptureMode thread safety ──────────────────────────────────────────

    [Fact]
    public async Task ReadingCaptureMode_WhileMutating_NeverThrows()
    {
        using var client = CreateClient();
        var exceptions = new List<Exception>();

        var mutator = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    client.EscalateToIncident("inc-1");
                    client.CloseIncident();
                }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
                await Task.Yield();
            }
        });

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                try { _ = client.CaptureMode; }
                catch (Exception ex) { lock (exceptions) { exceptions.Add(ex); } }
            }
        });

        await Task.WhenAll(mutator, reader);

        exceptions.Should().BeEmpty();
    }
}
