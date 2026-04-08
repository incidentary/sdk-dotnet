using BenchmarkDotNet.Attributes;
using Incidentary.Sdk.Redaction;

namespace Incidentary.Sdk.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RedactionBenchmarks
{
    private string _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _json = """{"user":"john","password":"secret123","token":"abc","data":{"nested_token":"xyz","value":42}}""";
    }

    [Benchmark]
    public string RedactJson() => PayloadRedactor.RedactJson(_json);
}
