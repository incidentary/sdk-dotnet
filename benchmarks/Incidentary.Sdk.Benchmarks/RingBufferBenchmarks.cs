using BenchmarkDotNet.Attributes;
using Incidentary.Sdk.Buffering;

namespace Incidentary.Sdk.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RingBufferBenchmarks
{
    private RingBuffer<int> _buffer = null!;

    [GlobalSetup]
    public void Setup() => _buffer = new RingBuffer<int>(4000);

    [Benchmark]
    public void WriteAndFlush()
    {
        for (int i = 0; i < 1000; i++)
            _buffer.Write(i);
        _buffer.Flush();
    }
}
