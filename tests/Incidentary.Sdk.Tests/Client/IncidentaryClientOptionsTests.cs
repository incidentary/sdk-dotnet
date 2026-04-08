using FluentAssertions;
using Incidentary.Sdk.Transport;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

public sealed class IncidentaryClientOptionsTests
{
    // ── Construction guards ────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new IncidentaryClient(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_EmptyApiKey_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "",
            ServiceName = "svc"
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ApiKey*");
    }

    [Fact]
    public void Constructor_WhitespaceApiKey_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "   ",
            ServiceName = "svc"
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ApiKey*");
    }

    [Fact]
    public void Constructor_EmptyServiceName_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = ""
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ServiceName*");
    }

    [Fact]
    public void Constructor_WhitespaceServiceName_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "  "
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ServiceName*");
    }

    [Fact]
    public void Constructor_ValidOptions_DoesNotThrow()
    {
        var act = () =>
        {
            using var _ = new IncidentaryClient(new IncidentaryClientOptions
            {
                ApiKey = "valid-key",
                ServiceName = "valid-service"
            });
        };

        act.Should().NotThrow();
    }

    // ── Default values ─────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_ApiKey_IsEmpty()
    {
        var opts = new IncidentaryClientOptions();

        opts.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public void DefaultOptions_ServiceName_IsEmpty()
    {
        var opts = new IncidentaryClientOptions();

        opts.ServiceName.Should().BeEmpty();
    }

    [Fact]
    public void DefaultOptions_Environment_IsProduction()
    {
        var opts = new IncidentaryClientOptions();

        opts.Environment.Should().Be("production");
    }

    [Fact]
    public void DefaultOptions_TimeoutMs_Is5000()
    {
        var opts = new IncidentaryClientOptions();

        opts.TimeoutMs.Should().Be(5_000);
    }

    [Fact]
    public void DefaultOptions_BufferCapacity_Is4000()
    {
        var opts = new IncidentaryClientOptions();

        opts.BufferCapacity.Should().Be(4_000);
    }

    [Fact]
    public void DefaultOptions_PreArmThresholdHigh_Is10()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmThresholdHigh.Should().Be(10.0);
    }

    [Fact]
    public void DefaultOptions_PreArmThresholdLow_Is2()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmThresholdLow.Should().Be(2.0);
    }

    [Fact]
    public void DefaultOptions_PreArmMinDurationMs_Is60000()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmMinDurationMs.Should().Be(60_000);
    }

    [Fact]
    public void DefaultOptions_PreArmTtlMs_Is300000()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmTtlMs.Should().Be(300_000);
    }

    [Fact]
    public void DefaultOptions_PreArmCooldownMs_Is30000()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmCooldownMs.Should().Be(30_000);
    }

    [Fact]
    public void DefaultOptions_SlowMinMs_Is250()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowMinMs.Should().Be(250);
    }

    [Fact]
    public void DefaultOptions_SlowMultiplier_Is2()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowMultiplier.Should().Be(2.0);
    }

    [Fact]
    public void DefaultOptions_SlowAlpha_Is01()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowAlpha.Should().Be(0.1);
    }

    [Fact]
    public void DefaultOptions_SlowSuccessRateHigh_Is20Percent()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowSuccessRateHigh.Should().Be(0.20);
    }

    [Fact]
    public void DefaultOptions_SlowSuccessRateMild_Is10Percent()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowSuccessRateMild.Should().Be(0.10);
    }

    [Fact]
    public void DefaultOptions_SlowMinSamples_Is50()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowMinSamples.Should().Be(50);
    }

    [Fact]
    public void DefaultOptions_SlowInclude4xx_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmSlowInclude4xxAsSuccessLike.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_InFlightMinAbs_Is32()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmInFlightMinAbs.Should().Be(32);
    }

    [Fact]
    public void DefaultOptions_InFlightMultiplier_Is2()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmInFlightMultiplier.Should().Be(2.0);
    }

    [Fact]
    public void DefaultOptions_InFlightNetGrowthMin_Is16()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmInFlightNetGrowthMin.Should().Be(16);
    }

    [Fact]
    public void DefaultOptions_InFlightHoldSecs_Is3()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmInFlightHoldSecs.Should().Be(3);
    }

    [Fact]
    public void DefaultOptions_InFlightMildHoldSecs_Is2()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmInFlightMildHoldSecs.Should().Be(2);
    }

    [Fact]
    public void DefaultOptions_RetryWindowMs_Is5000()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmRetryWindowMs.Should().Be(5_000);
    }

    [Fact]
    public void DefaultOptions_RetryRateHigh_Is10Percent()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmRetryRateHigh.Should().Be(0.10);
    }

    [Fact]
    public void DefaultOptions_RetryRateMild_Is5Percent()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmRetryRateMild.Should().Be(0.05);
    }

    [Fact]
    public void DefaultOptions_RetryMinTotal_Is20()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmRetryMinTotal.Should().Be(20);
    }

    [Fact]
    public void DefaultOptions_RetryTableSize_Is4096()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmRetryTableSize.Should().Be(4_096);
    }

    [Fact]
    public void DefaultOptions_DetailCaptureEnabled_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.DetailCaptureEnabled.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_DetailPayloadEnabled_IsFalse()
    {
        var opts = new IncidentaryClientOptions();

        opts.DetailPayloadEnabled.Should().BeFalse();
    }

    [Fact]
    public void DefaultOptions_DetailMaxPayloadBytes_Is4096()
    {
        var opts = new IncidentaryClientOptions();

        opts.DetailMaxPayloadBytes.Should().Be(4_096);
    }

    [Fact]
    public void DefaultOptions_AutoInstrument_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.AutoInstrument.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_OnError_IsNull()
    {
        var opts = new IncidentaryClientOptions();

        opts.OnError.Should().BeNull();
    }

    [Fact]
    public void DefaultOptions_EnableSlowSuccess_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmEnableSlowSuccess.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_EnableInFlight_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmEnableInFlight.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_EnableRetry_IsTrue()
    {
        var opts = new IncidentaryClientOptions();

        opts.PreArmEnableRetry.Should().BeTrue();
    }

    // ── Static allowlists ──────────────────────────────────────────────────

    [Fact]
    public void DefaultRequestHeaderAllowlist_ContainsContentType()
    {
        IncidentaryClientOptions.DefaultRequestHeaderAllowlist
            .Should().Contain("content-type");
    }

    [Fact]
    public void DefaultRequestHeaderAllowlist_ContainsUserAgent()
    {
        IncidentaryClientOptions.DefaultRequestHeaderAllowlist
            .Should().Contain("user-agent");
    }

    [Fact]
    public void DefaultResponseHeaderAllowlist_ContainsContentType()
    {
        IncidentaryClientOptions.DefaultResponseHeaderAllowlist
            .Should().Contain("content-type");
    }

    [Fact]
    public void DefaultResponseHeaderAllowlist_ContainsContentLength()
    {
        IncidentaryClientOptions.DefaultResponseHeaderAllowlist
            .Should().Contain("content-length");
    }

    // ── Security: TLS enforcement ──────────────────────────────────────────

    [Fact]
    public void Constructor_HttpBaseUrl_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            BaseUrl = "http://api.example.com"  // plaintext — must be rejected
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*HTTPS*");
    }

    [Fact]
    public void Constructor_HttpsBaseUrl_DoesNotThrow()
    {
        // HTTPS should be accepted (even if the server isn't running in tests)
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            BaseUrl = "https://api.example.com"
        });

        // May throw due to no running server, but NOT due to scheme validation
        act.Should().NotThrow<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullBaseUrl_DoesNotThrow()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            BaseUrl = null  // optional — no BaseUrl is fine
        });

        act.Should().NotThrow<ArgumentException>();
    }

    // ── Security: Numeric validation ──────────────────────────────────────

    [Fact]
    public void Constructor_ZeroBufferCapacity_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            BufferCapacity = 0
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BufferCapacity*");
    }

    [Fact]
    public void Constructor_NegativeBufferCapacity_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            BufferCapacity = -1
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BufferCapacity*");
    }

    [Fact]
    public void Constructor_ZeroTimeoutMs_ThrowsArgumentException()
    {
        var act = () => new IncidentaryClient(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            TimeoutMs = 0
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*TimeoutMs*");
    }
}
