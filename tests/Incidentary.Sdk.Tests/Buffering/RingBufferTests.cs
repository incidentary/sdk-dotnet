using FluentAssertions;
using Incidentary.Sdk.Buffering;
using Xunit;

namespace Incidentary.Sdk.Tests.Buffering;

public sealed class RingBufferTests
{
    [Fact]
    public void Constructor_WithValidCapacity_CreatesBuffer()
    {
        var buffer = new RingBuffer<int>(100);

        buffer.Count.Should().Be(0);
        buffer.Capacity.Should().Be(100);
        buffer.TotalWritten.Should().Be(0);
        buffer.TotalDropped.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithZeroCapacity_Throws()
    {
        var act = () => new RingBuffer<int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_Throws()
    {
        var act = () => new RingBuffer<int>(-5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Write_SingleItem_CountIsOne()
    {
        var buffer = new RingBuffer<int>(10);

        buffer.Write(42);

        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void Write_ToCapacity_CountEqualsCapacity()
    {
        const int capacity = 5;
        var buffer = new RingBuffer<int>(capacity);

        for (var i = 0; i < capacity; i++)
        {
            buffer.Write(i);
        }

        buffer.Count.Should().Be(capacity);
        buffer.TotalDropped.Should().Be(0);
    }

    [Fact]
    public void Write_PastCapacity_OverwritesOldest()
    {
        const int capacity = 5;
        var buffer = new RingBuffer<int>(capacity);

        for (var i = 0; i < capacity + 5; i++)
        {
            buffer.Write(i);
        }

        buffer.Count.Should().Be(capacity);
        buffer.TotalDropped.Should().Be(5);
        buffer.TotalWritten.Should().Be(capacity + 5);
    }

    [Fact]
    public void Flush_ReturnsItemsInWriteOrder()
    {
        var buffer = new RingBuffer<int>(10);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Write(i);
        }

        var items = buffer.Flush();

        items.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Flush_ClearsBuffer()
    {
        var buffer = new RingBuffer<int>(10);
        buffer.Write(1);
        buffer.Write(2);

        buffer.Flush();

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsEmptyList()
    {
        var buffer = new RingBuffer<int>(10);

        var items = buffer.Flush();

        items.Should().BeEmpty();
    }

    [Fact]
    public void Flush_AfterOverwrite_ReturnsNewestItems()
    {
        const int capacity = 5;
        var buffer = new RingBuffer<int>(capacity);

        // Write 10 items into a capacity-5 buffer: items 0..9
        // Oldest 5 (0..4) should be overwritten, leaving 5..9
        for (var i = 0; i < 10; i++)
        {
            buffer.Write(i);
        }

        var items = buffer.Flush();

        items.Should().Equal(5, 6, 7, 8, 9);
    }

    [Fact]
    public void Write_AfterFlush_WorksCorrectly()
    {
        var buffer = new RingBuffer<string>(5);
        buffer.Write("a");
        buffer.Write("b");
        buffer.Flush();

        buffer.Write("c");
        buffer.Write("d");

        buffer.Count.Should().Be(2);
        var items = buffer.Flush();
        items.Should().Equal("c", "d");
    }

    [Fact]
    public void TotalWritten_TracksAllWrites()
    {
        const int capacity = 3;
        var buffer = new RingBuffer<int>(capacity);

        for (var i = 0; i < 100; i++)
        {
            buffer.Write(i);
        }

        buffer.TotalWritten.Should().Be(100);
    }

    [Fact]
    public void ConcurrentWrites_NoDataLoss()
    {
        const int capacity = 5000;
        const int threadsCount = 10;
        const int writesPerThread = 1000;
        var buffer = new RingBuffer<int>(capacity);

        var threads = Enumerable.Range(0, threadsCount)
            .Select(t => new Thread(() =>
            {
                for (var i = 0; i < writesPerThread; i++)
                {
                    buffer.Write(t * writesPerThread + i);
                }
            }))
            .ToList();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        buffer.TotalWritten.Should().Be(threadsCount * writesPerThread);
        buffer.Count.Should().Be(capacity);
        buffer.TotalDropped.Should().Be(threadsCount * writesPerThread - capacity);
    }

    [Fact]
    public void Flush_WhileConcurrentWrites_NoException()
    {
        const int capacity = 100;
        var buffer = new RingBuffer<int>(capacity);
        var cts = new CancellationTokenSource();
        var exceptions = new List<Exception>();

        // Writer threads
        var writers = Enumerable.Range(0, 5)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    var value = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        buffer.Write(value++);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }))
            .ToList();

        // Flusher thread
        var flusher = new Thread(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    buffer.Flush();
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        foreach (var writer in writers)
        {
            writer.Start();
        }

        flusher.Start();

        Thread.Sleep(200);
        cts.Cancel();

        foreach (var writer in writers)
        {
            writer.Join();
        }

        flusher.Join();

        exceptions.Should().BeEmpty();
    }
}
