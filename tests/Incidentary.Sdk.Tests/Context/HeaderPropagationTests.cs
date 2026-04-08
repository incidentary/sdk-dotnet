using FluentAssertions;
using Incidentary.Sdk.Context;
using Xunit;

namespace Incidentary.Sdk.Tests.Context;

public sealed class HeaderPropagationTests
{
    [Fact]
    public void Inject_SetsHeaders()
    {
        var ctx = new TraceContext("trace-123", "ce-456");
        var headers = new Dictionary<string, string>();

        HeaderPropagation.Inject(headers, ctx);

        headers.Should().ContainKey(HeaderConstants.TraceIdHeader)
            .WhoseValue.Should().Be("trace-123");
        headers.Should().ContainKey(HeaderConstants.ParentCeHeader)
            .WhoseValue.Should().Be("ce-456");
    }

    [Fact]
    public void Extract_WithBothHeaders_ReturnsContext()
    {
        var headers = new Dictionary<string, string>
        {
            [HeaderConstants.TraceIdHeader] = "trace-abc",
            [HeaderConstants.ParentCeHeader] = "ce-def",
        };

        var result = HeaderPropagation.Extract(headers);

        result.Should().NotBeNull();
        result!.Value.TraceId.Should().Be("trace-abc");
        result.Value.CeId.Should().Be("ce-def");
    }

    [Fact]
    public void Extract_MissingTraceId_ReturnsNull()
    {
        var headers = new Dictionary<string, string>
        {
            [HeaderConstants.ParentCeHeader] = "ce-def",
        };

        var result = HeaderPropagation.Extract(headers);

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_MissingParentCe_ReturnsContextWithGeneratedCeId()
    {
        var headers = new Dictionary<string, string>
        {
            [HeaderConstants.TraceIdHeader] = "trace-abc",
        };

        var result = HeaderPropagation.Extract(headers);

        result.Should().NotBeNull();
        result!.Value.TraceId.Should().Be("trace-abc");
        result.Value.CeId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Extract_NoHeaders_ReturnsNull()
    {
        var headers = new Dictionary<string, string>();

        var result = HeaderPropagation.Extract(headers);

        result.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_InjectThenExtract()
    {
        var original = new TraceContext("trace-rt", "ce-rt");
        var headers = new Dictionary<string, string>();

        HeaderPropagation.Inject(headers, original);
        var extracted = HeaderPropagation.Extract(headers);

        extracted.Should().NotBeNull();
        extracted!.Value.Should().Be(original);
    }

    [Fact]
    public void Extract_WithFuncOverload()
    {
        var headers = new Dictionary<string, string>
        {
            [HeaderConstants.TraceIdHeader] = "trace-func",
            [HeaderConstants.ParentCeHeader] = "ce-func",
        };

        Func<string, string?> getter = key =>
            headers.TryGetValue(key, out var value) ? value : null;

        var result = HeaderPropagation.Extract(getter);

        result.Should().NotBeNull();
        result!.Value.TraceId.Should().Be("trace-func");
        result.Value.CeId.Should().Be("ce-func");
    }

    [Fact]
    public void Inject_WithActionOverload()
    {
        var ctx = new TraceContext("trace-action", "ce-action");
        var headers = new Dictionary<string, string>();

        Action<string, string> setter = (key, value) => headers[key] = value;

        HeaderPropagation.Inject(setter, ctx);

        headers.Should().ContainKey(HeaderConstants.TraceIdHeader)
            .WhoseValue.Should().Be("trace-action");
        headers.Should().ContainKey(HeaderConstants.ParentCeHeader)
            .WhoseValue.Should().Be("ce-action");
    }
}
