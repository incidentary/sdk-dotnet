using BenchmarkDotNet.Attributes;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SerializationBenchmarks
{
    private CausalEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _event = new CausalEvent
        {
            Id = Guid.NewGuid().ToString(),
            TraceId = Guid.NewGuid().ToString(),
            ServiceId = "bench-service",
            OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
            Kind = CeKind.HttpIn,
            Type = EventTypes.HttpServer,
            StatusCode = 200,
            DurationNs = 45_000_000,
        };
    }

    [Benchmark]
    public string SerializeEvent()
    {
        return System.Text.Json.JsonSerializer.Serialize(_event, WireJson.Options);
    }
}
