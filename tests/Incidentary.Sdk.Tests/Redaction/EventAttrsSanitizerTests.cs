using FluentAssertions;
using Incidentary.Sdk.Redaction;
using Xunit;

namespace Incidentary.Sdk.Tests.Redaction;

public sealed class EventAttrsSanitizerTests
{
    [Fact]
    public void Sanitize_Null_ReturnsNull()
    {
        var result = EventAttrsSanitizer.Sanitize(null);

        result.Should().BeNull();
    }

    [Fact]
    public void Sanitize_ValidAttrs_ReturnsCopy()
    {
        var original = new Dictionary<string, object> { ["key"] = "value" };

        var result = EventAttrsSanitizer.Sanitize(original);

        result.Should().NotBeSameAs(original);
        result.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Sanitize_StringValue_Kept()
    {
        var attrs = new Dictionary<string, object> { ["name"] = "alice" };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result["name"].Should().Be("alice");
    }

    [Fact]
    public void Sanitize_IntValue_Kept()
    {
        var attrs = new Dictionary<string, object> { ["count"] = 42 };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result["count"].Should().Be(42);
    }

    [Fact]
    public void Sanitize_BoolValue_Kept()
    {
        var attrs = new Dictionary<string, object> { ["active"] = true };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result["active"].Should().Be(true);
    }

    [Fact]
    public void Sanitize_DoubleValue_Kept()
    {
        var attrs = new Dictionary<string, object> { ["ratio"] = 3.14 };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result["ratio"].Should().Be(3.14);
    }

    [Fact]
    public void Sanitize_LongStringValue_Truncated()
    {
        var longValue = new string('x', 2000);
        var attrs = new Dictionary<string, object> { ["desc"] = longValue };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        ((string)result["desc"]).Length.Should().Be(EventAttrsSanitizer.MaxStringValueLength);
    }

    [Fact]
    public void Sanitize_NestedObject_Dropped()
    {
        var attrs = new Dictionary<string, object>
        {
            ["name"] = "alice",
            ["nested"] = new Dictionary<string, object> { ["inner"] = "value" },
        };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result.Should().ContainKey("name");
        result.Should().NotContainKey("nested");
    }

    [Fact]
    public void Sanitize_ArrayValue_Dropped()
    {
        var attrs = new Dictionary<string, object>
        {
            ["name"] = "alice",
            ["tags"] = new List<string> { "a", "b" },
        };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result.Should().ContainKey("name");
        result.Should().NotContainKey("tags");
    }

    [Fact]
    public void Sanitize_MoreThan32Keys_Truncated()
    {
        var attrs = new Dictionary<string, object>();
        for (var i = 0; i < 50; i++)
        {
            attrs[$"key_{i}"] = i;
        }

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result.Count.Should().Be(EventAttrsSanitizer.MaxKeys);
    }

    [Fact]
    public void Sanitize_NullValue_Kept()
    {
        var attrs = new Dictionary<string, object> { ["empty"] = null! };

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result.Should().ContainKey("empty");
        result["empty"].Should().BeNull();
    }

    [Fact]
    public void Sanitize_EmptyDict_ReturnsEmpty()
    {
        var attrs = new Dictionary<string, object>();

        var result = EventAttrsSanitizer.Sanitize(attrs)!;

        result.Should().BeEmpty();
        result.Should().NotBeSameAs(attrs);
    }
}
