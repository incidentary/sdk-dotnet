using BenchmarkDotNet.Attributes;
using Incidentary.Sdk.Redaction;

namespace Incidentary.Sdk.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class EventAttrsBenchmarks
{
    private Dictionary<string, object> _attrs = null!;

    [GlobalSetup]
    public void Setup()
    {
        _attrs = new Dictionary<string, object>();
        for (int i = 0; i < 30; i++)
            _attrs[$"key_{i}"] = $"value_{i}";
    }

    [Benchmark]
    public Dictionary<string, object>? SanitizeAttrs() => EventAttrsSanitizer.Sanitize(_attrs);
}
