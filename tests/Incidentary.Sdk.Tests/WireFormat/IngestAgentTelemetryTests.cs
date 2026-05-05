using System.Text.Json;
using FluentAssertions;
using Incidentary.Sdk.WireFormat;
using Xunit;

namespace Incidentary.Sdk.Tests.WireFormat;

/// <summary>
/// Tests that IngestAgent includes the new adaptive batch telemetry fields
/// (flush_latency_ema_ms and current_batch_size) in the wire format.
/// </summary>
public sealed class IngestAgentTelemetryTests
{
    [Fact]
    public void IngestAgent_FlushLatencyEmaMs_DefaultsToZero()
    {
        var agent = new IngestAgent { SdkVersion = "1.0.0" };

        agent.FlushLatencyEmaMs.Should().Be(0);
    }

    [Fact]
    public void IngestAgent_CurrentBatchSize_DefaultsToZero()
    {
        var agent = new IngestAgent { SdkVersion = "1.0.0" };

        agent.CurrentBatchSize.Should().Be(0);
    }

    [Fact]
    public void IngestAgent_FlushLatencyEmaMs_CanBeSet()
    {
        var agent = new IngestAgent
        {
            SdkVersion = "1.0.0",
            FlushLatencyEmaMs = 42.5
        };

        agent.FlushLatencyEmaMs.Should().Be(42.5);
    }

    [Fact]
    public void IngestAgent_CurrentBatchSize_CanBeSet()
    {
        var agent = new IngestAgent
        {
            SdkVersion = "1.0.0",
            CurrentBatchSize = 600
        };

        agent.CurrentBatchSize.Should().Be(600);
    }

    [Fact]
    public void IngestAgent_Serialization_IncludesNewFields()
    {
        var agent = new IngestAgent
        {
            SdkVersion = "1.0.0",
            FlushLatencyEmaMs = 55.3,
            CurrentBatchSize = 750
        };

        var json = JsonSerializer.Serialize(agent, WireJson.Options);

        json.Should().Contain("\"flush_latency_ema_ms\"");
        json.Should().Contain("\"current_batch_size\"");

        // Deserialize and verify round-trip
        var deserialized = JsonSerializer.Deserialize<IngestAgent>(json, WireJson.Options);
        deserialized.Should().NotBeNull();
        deserialized!.FlushLatencyEmaMs.Should().Be(55.3);
        deserialized.CurrentBatchSize.Should().Be(750);
    }
}
