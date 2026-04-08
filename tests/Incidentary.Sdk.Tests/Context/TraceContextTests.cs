using FluentAssertions;
using Incidentary.Sdk.Context;
using Xunit;

namespace Incidentary.Sdk.Tests.Context;

public sealed class TraceContextTests
{
    [Fact]
    public void NewRoot_GeneratesUniqueIds()
    {
        var root1 = TraceContext.NewRoot();
        var root2 = TraceContext.NewRoot();

        root1.TraceId.Should().NotBeNullOrWhiteSpace();
        root1.CeId.Should().NotBeNullOrWhiteSpace();
        root2.TraceId.Should().NotBeNullOrWhiteSpace();
        root2.CeId.Should().NotBeNullOrWhiteSpace();

        root1.TraceId.Should().NotBe(root2.TraceId);
        root1.CeId.Should().NotBe(root2.CeId);
    }

    [Fact]
    public void NewChild_KeepsTraceId()
    {
        var parent = TraceContext.NewRoot();
        var child = parent.NewChild();

        child.TraceId.Should().Be(parent.TraceId);
        child.CeId.Should().NotBe(parent.CeId);
        child.CeId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RecordEquality()
    {
        var ctx1 = new TraceContext("trace-1", "ce-1");
        var ctx2 = new TraceContext("trace-1", "ce-1");
        var ctx3 = new TraceContext("trace-1", "ce-2");

        ctx1.Should().Be(ctx2);
        ctx1.Should().NotBe(ctx3);
    }
}
