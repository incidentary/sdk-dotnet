using System.Text.RegularExpressions;
using FluentAssertions;
using Incidentary.Sdk;
using Xunit;

namespace Incidentary.Sdk.Tests;

/// <summary>
/// Tests for <see cref="IncidentaryIds.NewId"/> — canonical UUIDv4 helper.
/// </summary>
public sealed class IncidentaryIdsTests
{
    // Canonical UUIDv4 shape: version nibble '4' at index 14,
    // RFC 4122 variant bits (8/9/a/b) at index 19.
    private static readonly Regex Uuidv4Pattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NewId_MatchesUuidv4Pattern()
    {
        var id = IncidentaryIds.NewId();
        Uuidv4Pattern.IsMatch(id).Should().BeTrue($"generated id was {id}");
    }

    [Fact]
    public void NewId_VersionNibbleIsFour()
    {
        // The whole point of unifying on v4: a future refactor that
        // silently reinstates v7 must not slip through review. This
        // test screams the moment the version nibble stops being '4'.
        var id = IncidentaryIds.NewId();
        id[14].Should().Be('4');
    }

    [Fact]
    public void NewId_VariantBitsMatchRfc4122()
    {
        var id = IncidentaryIds.NewId();
        "89ab".Should().Contain(char.ToLowerInvariant(id[19]).ToString());
    }

    [Fact]
    public void NewId_DoesNotCollideAcrossManySamples()
    {
        // v4 has 122 random bits; collision across 4096 samples is
        // effectively zero. A collision here proves the RNG is seeded
        // or deterministic — a fatal bug for bearer-token use.
        var ids = new HashSet<string>();
        for (var i = 0; i < 4096; i++)
        {
            ids.Add(IncidentaryIds.NewId()).Should().BeTrue();
        }
        ids.Should().HaveCount(4096);
    }

    [Fact]
    public void NewId_IsNotSeriallyOrdered()
    {
        // v4 has no embedded timestamp, so two ids generated
        // back-to-back must not have a systematic lexicographic
        // relationship. Guards against a regression that reinstates
        // a time-ordered generator.
        //
        // We sample 500 pairs; a<b and a>b should each land in
        // roughly [150, 350] — well inside a 12σ envelope around
        // 250/500.
        var lt = 0;
        var gt = 0;
        for (var i = 0; i < 500; i++)
        {
            var a = IncidentaryIds.NewId();
            var b = IncidentaryIds.NewId();
            var cmp = string.Compare(a, b, StringComparison.Ordinal);
            if (cmp < 0) lt++;
            else if (cmp > 0) gt++;
            else throw new Xunit.Sdk.XunitException($"impossible collision at {i}: {a}");
        }
        lt.Should().BeInRange(150, 350, "a<b should be roughly uniform");
        gt.Should().BeInRange(150, 350, "a>b should be roughly uniform");
    }

    [Fact]
    public void NewId_IsCanonical36CharShape()
    {
        var id = IncidentaryIds.NewId();
        id.Length.Should().Be(36);
        foreach (var i in new[] { 8, 13, 18, 23 })
        {
            id[i].Should().Be('-', $"expected hyphen at index {i}");
        }
    }
}
