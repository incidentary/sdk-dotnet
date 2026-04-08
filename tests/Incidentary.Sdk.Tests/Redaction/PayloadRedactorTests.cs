using System.Text.Json;
using FluentAssertions;
using Incidentary.Sdk.Redaction;
using Xunit;

namespace Incidentary.Sdk.Tests.Redaction;

public sealed class PayloadRedactorTests
{
    private static readonly string[] ExtraCustomFields = ["custom_secret"];
    [Fact]
    public void RedactJson_PasswordField_Redacted()
    {
        var input = """{"password":"secret"}""";

        var result = PayloadRedactor.RedactJson(input);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("password").GetString().Should().Be(PayloadRedactor.RedactedValue);
    }

    [Fact]
    public void RedactJson_NestedSensitiveField_Redacted()
    {
        var input = """{"user":{"token":"abc123"}}""";

        var result = PayloadRedactor.RedactJson(input);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("user").GetProperty("token").GetString()
            .Should().Be(PayloadRedactor.RedactedValue);
    }

    [Fact]
    public void RedactJson_CaseInsensitive()
    {
        var input = """{"Password":"secret","TOKEN":"abc"}""";

        var result = PayloadRedactor.RedactJson(input);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Password").GetString().Should().Be(PayloadRedactor.RedactedValue);
        doc.RootElement.GetProperty("TOKEN").GetString().Should().Be(PayloadRedactor.RedactedValue);
    }

    [Fact]
    public void RedactJson_NoSensitiveFields_Unchanged()
    {
        var input = """{"name":"john","age":30}""";

        var result = PayloadRedactor.RedactJson(input);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("name").GetString().Should().Be("john");
        doc.RootElement.GetProperty("age").GetInt32().Should().Be(30);
    }

    [Fact]
    public void RedactJson_CustomExtraFields_Redacted()
    {
        var input = """{"custom_secret":"value","name":"john"}""";

        var result = PayloadRedactor.RedactJson(input, ExtraCustomFields);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("custom_secret").GetString().Should().Be(PayloadRedactor.RedactedValue);
        doc.RootElement.GetProperty("name").GetString().Should().Be("john");
    }

    [Fact]
    public void RedactJson_InvalidJson_ReturnsInput()
    {
        var input = "not json at all";

        var result = PayloadRedactor.RedactJson(input);

        result.Should().Be(input);
    }

    [Fact]
    public void RedactJson_EmptyString_ReturnsEmpty()
    {
        var result = PayloadRedactor.RedactJson("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void RedactJson_ArrayValues_RedactsInArrayObjects()
    {
        var input = """[{"token":"x"},{"name":"safe"}]""";

        var result = PayloadRedactor.RedactJson(input);

        var doc = JsonDocument.Parse(result);
        var arr = doc.RootElement;
        arr[0].GetProperty("token").GetString().Should().Be(PayloadRedactor.RedactedValue);
        arr[1].GetProperty("name").GetString().Should().Be("safe");
    }

    [Fact]
    public void RedactUrlParams_SensitiveParam_Redacted()
    {
        var input = "password=secret&name=john";

        var result = PayloadRedactor.RedactUrlParams(input);

        result.Should().Be($"password={PayloadRedactor.RedactedValue}&name=john");
    }

    [Fact]
    public void RedactUrlParams_CaseInsensitive()
    {
        var input = "TOKEN=abc";

        var result = PayloadRedactor.RedactUrlParams(input);

        result.Should().Be($"TOKEN={PayloadRedactor.RedactedValue}");
    }

    [Fact]
    public void RedactUrlParams_NoMatch_Unchanged()
    {
        var input = "foo=bar&baz=qux";

        var result = PayloadRedactor.RedactUrlParams(input);

        result.Should().Be(input);
    }

    [Fact]
    public void RedactUrlParams_EmptyString_ReturnsEmpty()
    {
        var result = PayloadRedactor.RedactUrlParams("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Truncate_ShortString_Unchanged()
    {
        var input = "Hello, world!";

        var result = PayloadRedactor.Truncate(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Truncate_LongString_Truncated()
    {
        var input = new string('a', 10000);

        var result = PayloadRedactor.Truncate(input, 4096);

        System.Text.Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(4096);
    }

    [Fact]
    public void Truncate_MultiByte_RespectsBoundary()
    {
        // Each emoji (U+1F600) is 4 bytes in UTF-8 and 2 chars (surrogate pair) in C#.
        // 1024 emojis = 4096 bytes. Add one more to exceed the limit.
        var emoji = char.ConvertFromUtf32(0x1F600); // surrogate pair
        var sb = new System.Text.StringBuilder(1025 * 2);
        for (var i = 0; i < 1025; i++) sb.Append(emoji);
        var input = sb.ToString(); // 4100 bytes

        var result = PayloadRedactor.Truncate(input, 4096);

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(result);
        byteCount.Should().BeLessThanOrEqualTo(4096);
        // Must not produce invalid chars — round-trip should work
        var roundTrip = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(result));
        roundTrip.Should().Be(result);
    }

    // ── Branch coverage for edge cases ─────────────────────────────────────

    [Fact]
    public void Truncate_EmptyString_ReturnsEmpty()
    {
        // Covers line 97 true branch: `if (string.IsNullOrEmpty(input)) return input`
        var result = PayloadRedactor.Truncate("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Truncate_NullString_ReturnsNull()
    {
        // Also covers `string.IsNullOrEmpty` true branch with null input.
        var result = PayloadRedactor.Truncate(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void Truncate_SurrogatePairAtExactBoundary_BacksUpToAvoidSplit()
    {
        // Covers line 127 true branch: `if (lo > 0 && char.IsHighSurrogate(input[lo - 1]))`
        // Use maxBytes=4095 with 1025 emojis (each 4 bytes UTF-8 / 2 C# chars).
        // Binary search: GetByteCount(input[0..2047]) = 4092 + 3 = 4095 <= 4095 → lo=2047.
        // IsHighSurrogate(input[2046]) = true (high surrogate of emoji #1023) → lo-- → lo=2046.
        var emoji = char.ConvertFromUtf32(0x1F600);
        var sb = new System.Text.StringBuilder(1025 * 2);
        for (var i = 0; i < 1025; i++) sb.Append(emoji);
        var input = sb.ToString();

        var result = PayloadRedactor.Truncate(input, 4095);

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(result);
        byteCount.Should().BeLessThanOrEqualTo(4095);
        // Result must not end with a lone high surrogate
        result.Should().EndWith(emoji);
    }

    [Fact]
    public void RedactUrlParams_ParamWithoutEqualsSign_PassedThrough()
    {
        // Covers line 75 true branch: `if (eqIndex < 0)` — the continue path.
        // A query param with no '=' is treated as a flag and passed through unchanged.
        var input = "debug&password=secret&verbose";

        var result = PayloadRedactor.RedactUrlParams(input);

        result.Should().Be($"debug&password={PayloadRedactor.RedactedValue}&verbose");
    }
}
