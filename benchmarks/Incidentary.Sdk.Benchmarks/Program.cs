using BenchmarkDotNet.Running;
using Incidentary.Sdk.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RingBufferBenchmarks).Assembly).Run(args);
