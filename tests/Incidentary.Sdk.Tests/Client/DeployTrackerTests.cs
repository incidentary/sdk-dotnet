using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Incidentary.Sdk.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Incidentary.Sdk.Tests.Client;

/// <summary>
/// Verifies <see cref="DeployTracker.TrackAsync"/> fail-open behavior,
/// snake_case body shape, default values, and header format.
/// </summary>
public sealed class DeployTrackerTests
{
    private static HttpClient BuildClientCapturing(out CapturingHandler handler, HttpStatusCode status = HttpStatusCode.Accepted)
    {
        handler = new CapturingHandler(status);
        return new HttpClient(handler);
    }

    private static TrackDeployConfig Cfg(HttpClient httpClient)
        => new(BaseUrl: "https://api.incidentary.dev", ApiKey: "ik_test_deadbeef", HttpClient: httpClient);

    [Fact]
    public async Task TrackAsync_PostsToDeploysEndpointWithSnakeCaseBody()
    {
        using var http = BuildClientCapturing(out var handler);

        await DeployTracker.TrackAsync(
            Cfg(http),
            new TrackDeployOptions(
                Service: "payments-api",
                Version: "1.2.3",
                CommitSha: "abc1234",
                CommitMessage: "fix rounding",
                Branch: "main",
                DeployedByName: "Ada",
                DeployedByEmail: "ada@example.com",
                Environment: "staging",
                DiffUrl: "https://github.com/org/repo/compare/abc1234",
                Metadata: new Dictionary<string, object?> { ["pipeline"] = "ci-123" }));

        handler.Calls.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/deploys");

        var body = handler.LastBody!.Value;
        body.GetProperty("service_name").GetString().Should().Be("payments-api");
        body.GetProperty("version").GetString().Should().Be("1.2.3");
        body.GetProperty("commit_sha").GetString().Should().Be("abc1234");
        body.GetProperty("commit_message").GetString().Should().Be("fix rounding");
        body.GetProperty("branch").GetString().Should().Be("main");
        body.GetProperty("deployed_by_name").GetString().Should().Be("Ada");
        body.GetProperty("deployed_by_email").GetString().Should().Be("ada@example.com");
        body.GetProperty("environment").GetString().Should().Be("staging");
        body.GetProperty("deploy_source").GetString().Should().Be("sdk");
        body.GetProperty("diff_url").GetString().Should().Be("https://github.com/org/repo/compare/abc1234");
    }

    [Fact]
    public async Task TrackAsync_AttachesAuthorizationAndContentTypeHeaders()
    {
        using var http = BuildClientCapturing(out var handler);
        await DeployTracker.TrackAsync(
            Cfg(http) with { ApiKey = "ik_live_xyz" },
            new TrackDeployOptions(Service: "payments-api"));

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("ik_live_xyz");
        handler.LastRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task TrackAsync_DefaultsEnvironmentToProduction()
    {
        using var http = BuildClientCapturing(out var handler);
        await DeployTracker.TrackAsync(Cfg(http), new TrackDeployOptions(Service: "payments-api"));

        handler.LastBody!.Value.GetProperty("environment").GetString().Should().Be("production");
    }

    [Fact]
    public async Task TrackAsync_AlwaysSetsDeploySourceToSdk()
    {
        using var http = BuildClientCapturing(out var handler);
        await DeployTracker.TrackAsync(Cfg(http), new TrackDeployOptions(Service: "payments-api"));

        handler.LastBody!.Value.GetProperty("deploy_source").GetString().Should().Be("sdk");
    }

    [Fact]
    public async Task TrackAsync_DeployedAtDefaultsToNowAsIso8601()
    {
        using var http = BuildClientCapturing(out var handler);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await DeployTracker.TrackAsync(Cfg(http), new TrackDeployOptions(Service: "payments-api"));
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var raw = handler.LastBody!.Value.GetProperty("deployed_at").GetString();
        raw.Should().NotBeNullOrEmpty();
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            .Should().BeTrue();
        parsed.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task TrackAsync_DeployedAtCustomValuePassesThrough()
    {
        using var http = BuildClientCapturing(out var handler);
        var fixedAt = new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero);
        await DeployTracker.TrackAsync(Cfg(http),
            new TrackDeployOptions(Service: "payments-api", DeployedAt: fixedAt));

        handler.LastBody!.Value.GetProperty("deployed_at").GetString()
            .Should().StartWith("2026-04-01T09:30:00");
    }

    [Fact]
    public async Task TrackAsync_OmitsUnsetOptionalFieldsFromBody()
    {
        using var http = BuildClientCapturing(out var handler);
        await DeployTracker.TrackAsync(Cfg(http), new TrackDeployOptions(Service: "payments-api"));

        var body = handler.LastBody!.Value;
        foreach (var key in new[] { "version", "commit_sha", "commit_message", "branch", "deployed_by_name", "deployed_by_email", "diff_url" })
        {
            body.TryGetProperty(key, out _).Should().BeFalse($"body should not include {key} when unset");
        }
    }

    [Fact]
    public async Task TrackAsync_TrimsTrailingSlashFromBaseUrl()
    {
        using var http = BuildClientCapturing(out var handler);
        var cfg = Cfg(http) with { BaseUrl = "https://api.incidentary.dev/" };
        await DeployTracker.TrackAsync(cfg, new TrackDeployOptions(Service: "payments-api"));

        handler.LastRequest!.RequestUri!.ToString().Should()
            .Be("https://api.incidentary.dev/api/v1/deploys");
    }

    [Fact]
    public async Task TrackAsync_NetworkErrorDoesNotThrow()
    {
        var handler = new ThrowingHandler();
        using var http = new HttpClient(handler);

        var invoked = 0;
        var logger = new CapturingLogger();

        var action = async () => await DeployTracker.TrackAsync(
            new TrackDeployConfig("https://api.incidentary.dev", "ik", http) { Logger = logger },
            new TrackDeployOptions(Service: "payments-api"));

        await action.Should().NotThrowAsync();
        invoked = logger.Records.Count;
        invoked.Should().BeGreaterThan(0);
        logger.Records[0].Message.Should().Contain("TrackDeploy");
    }

    [Fact]
    public async Task TrackAsync_HttpErrorStatusDoesNotThrow()
    {
        using var http = BuildClientCapturing(out var handler, HttpStatusCode.ServiceUnavailable);
        var logger = new CapturingLogger();

        var action = async () => await DeployTracker.TrackAsync(
            new TrackDeployConfig("https://api.incidentary.dev", "ik", http) { Logger = logger },
            new TrackDeployOptions(Service: "payments-api"));

        await action.Should().NotThrowAsync();
        logger.Records.Should().ContainSingle(r => r.Message.Contains("503", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrackAsync_EmptyServiceDoesNotCallNetwork()
    {
        using var http = BuildClientCapturing(out var handler);

        await DeployTracker.TrackAsync(Cfg(http),
            new TrackDeployOptions(Service: string.Empty));

        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task TrackAsync_WorksWithoutExplicitLogger()
    {
        var handler = new ThrowingHandler();
        using var http = new HttpClient(handler);
        var cfg = new TrackDeployConfig("https://api.incidentary.dev", "ik", http);

        var action = async () => await DeployTracker.TrackAsync(cfg,
            new TrackDeployOptions(Service: "payments-api"));

        await action.Should().NotThrowAsync();
    }

    // --- Test doubles -----------------------------------------------------

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public JsonElement? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                LastBody = doc.RootElement.Clone();
            }
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogRecord> Records { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Records.Add(new LogRecord(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogRecord(LogLevel Level, string Message);
}
