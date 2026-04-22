using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Incidentary.Sdk;
using Xunit;

namespace Incidentary.Sdk.Tests;

/// <summary>
/// Tests for <see cref="IncidentaryIds.NewId"/> (UUIDv7) and
/// <see cref="IncidentaryIds.NewRandomToken"/> (UUIDv4).
/// </summary>
public sealed class IncidentaryIdsTests
{
    private static readonly Regex Uuidv7Pattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Uuidv4Pattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NewId_MatchesUuidv7Pattern()
    {
        var id = IncidentaryIds.NewId();
        Uuidv7Pattern.IsMatch(id).Should().BeTrue($"generated id was {id}");
    }

    [Fact]
    public void NewId_VersionNibbleIsSeven()
    {
        var id = IncidentaryIds.NewId();
        // Canonical layout: position 14 is the version nibble.
        id[14].Should().Be('7');
    }

    [Fact]
    public void NewId_VariantBitsMatchRfc4122()
    {
        var id = IncidentaryIds.NewId();
        // Position 19 is the variant nibble.
        "89ab".Should().Contain(char.ToLowerInvariant(id[19]).ToString());
    }

    [Fact]
    public async Task NewId_IsTimeOrderedAcrossSmallDelay()
    {
        var a = IncidentaryIds.NewId();
        await Task.Delay(2);
        var b = IncidentaryIds.NewId();

        // v7 encodes wall-clock ms in leading bits; lexicographic
        // order matches chronological order.
        string.Compare(a, b, StringComparison.Ordinal).Should().BeLessThan(0);
    }

    [Fact]
    public void NewId_IsUniqueWithinMillisecond()
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 256; i++)
        {
            ids.Add(IncidentaryIds.NewId()).Should().BeTrue();
        }
        ids.Should().HaveCount(256);
    }

    [Fact]
    public void NewId_TimestampIsCloseToCurrentTime()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = IncidentaryIds.NewId();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // First 12 hex chars (48 bits) encode Unix-epoch ms.
        var tsHex = string.Concat(id.AsSpan(0, 8), id.AsSpan(9, 4));
        var ts = long.Parse(tsHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        ts.Should().BeInRange(before - 5_000, after + 5_000);
    }

    [Fact]
    public void NewRandomToken_MatchesUuidv4Pattern()
    {
        var tok = IncidentaryIds.NewRandomToken();
        Uuidv4Pattern.IsMatch(tok).Should().BeTrue($"generated token was {tok}");
    }

    [Fact]
    public void NewRandomToken_VersionNibbleIsFour()
    {
        var tok = IncidentaryIds.NewRandomToken();
        tok[14].Should().Be('4');
    }

    [Fact]
    public void NewRandomToken_VariantBitsMatchRfc4122()
    {
        var tok = IncidentaryIds.NewRandomToken();
        "89ab".Should().Contain(char.ToLowerInvariant(tok[19]).ToString());
    }

    [Fact]
    public void NewRandomToken_NeverReusesV7VersionNibble()
    {
        for (var i = 0; i < 64; i++)
        {
            var tok = IncidentaryIds.NewRandomToken();
            tok[14].Should().NotBe('7', $"iteration {i} returned v7 layout: {tok}");
        }
    }

    [Fact]
    public void NewRandomToken_IsUniqueAcrossManyCalls()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 512; i++)
        {
            seen.Add(IncidentaryIds.NewRandomToken()).Should().BeTrue(
                $"iteration {i} produced duplicate token");
        }
        seen.Should().HaveCount(512);
    }

    [Fact]
    public async Task NewRandomToken_IsNotMonotonicByGenerationTime()
    {
        // Over 40 pairs we MUST see both orderings. An impl returning
        // v7 would always satisfy a < b.
        var sawAsc = false;
        var sawDescOrEq = false;
        for (var i = 0; i < 40; i++)
        {
            var a = IncidentaryIds.NewRandomToken();
            await Task.Delay(2);
            var b = IncidentaryIds.NewRandomToken();
            if (string.Compare(a, b, StringComparison.Ordinal) < 0)
            {
                sawAsc = true;
            }
            else
            {
                sawDescOrEq = true;
            }
            if (sawAsc && sawDescOrEq)
            {
                return;
            }
        }
        throw new Xunit.Sdk.XunitException(
            "v4 tokens must not be monotonic by generation time");
    }
}
