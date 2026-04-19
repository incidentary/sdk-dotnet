using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incidentary.Sdk.Client;

/// <summary>
/// Minimal transport config for <see cref="DeployTracker.TrackAsync"/>.
/// Kept decoupled from <see cref="IncidentaryClient"/> so CI scripts
/// can track a deploy without constructing the full instrumented client.
/// </summary>
/// <param name="BaseUrl">API base URL, e.g. https://api.incidentary.dev.</param>
/// <param name="ApiKey">Workspace API key used as a bearer token.</param>
/// <param name="HttpClient">Optional <see cref="HttpClient"/> — one is created if omitted.</param>
public sealed record TrackDeployConfig(
    string BaseUrl,
    string ApiKey,
    HttpClient? HttpClient = null)
{
    /// <summary>
    /// Optional logger for fail-open warnings. Uses <see cref="NullLogger.Instance"/>
    /// when not supplied — failures are swallowed silently.
    /// </summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;

    /// <summary>Request timeout. Defaults to 5 seconds when a fresh HttpClient is created.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Describes a single deploy event. Only <see cref="Service"/> is required.
/// Unset optional fields are omitted from the request body rather than
/// sent as JSON <c>null</c>.
/// </summary>
public sealed record TrackDeployOptions(
    string Service,
    string? Version = null,
    string? CommitSha = null,
    string? CommitMessage = null,
    string? Branch = null,
    string? DeployedByName = null,
    string? DeployedByEmail = null,
    DateTimeOffset? DeployedAt = null,
    string? Environment = null,
    string? DiffUrl = null,
    IDictionary<string, object?>? Metadata = null);

/// <summary>
/// Record a deployment with Incidentary. Fail-open by design: every
/// failure mode (transport error, 4xx/5xx, timeout, invalid response)
/// logs a warning and returns without throwing. A broken deploy
/// tracker must never break a deploy.
/// </summary>
public static class DeployTracker
{
    private const string EndpointPath = "/api/v1/deploys";

    /// <summary>Post a deploy record to Incidentary.</summary>
    public static async Task TrackAsync(
        TrackDeployConfig config,
        TrackDeployOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Service))
        {
            config.Logger.LogWarning("incidentary.TrackDeploy: service is required — skipping");
            return;
        }

        var url = config.BaseUrl.TrimEnd('/') + EndpointPath;
        var body = BuildBody(options);

        HttpClient? created = null;
        var client = config.HttpClient;
        if (client is null)
        {
            created = new HttpClient { Timeout = config.Timeout };
            client = created;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: SerializerOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                config.Logger.LogWarning(
                    "incidentary.TrackDeploy failed: HTTP {StatusCode}",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or JsonException
            or InvalidOperationException)
        {
            config.Logger.LogWarning(ex, "incidentary.TrackDeploy failed: {Message}", ex.Message);
        }
        finally
        {
            created?.Dispose();
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static Dictionary<string, object?> BuildBody(TrackDeployOptions options)
    {
        var deployedAt = options.DeployedAt ?? DateTimeOffset.UtcNow;
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["service_name"] = options.Service,
            ["deploy_source"] = "sdk",
            ["environment"] = string.IsNullOrEmpty(options.Environment) ? "production" : options.Environment,
            ["deployed_at"] = deployedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["metadata"] = options.Metadata ?? new Dictionary<string, object?>(),
        };

        SetIfPresent(body, "version", options.Version);
        SetIfPresent(body, "commit_sha", options.CommitSha);
        SetIfPresent(body, "commit_message", options.CommitMessage);
        SetIfPresent(body, "branch", options.Branch);
        SetIfPresent(body, "deployed_by_name", options.DeployedByName);
        SetIfPresent(body, "deployed_by_email", options.DeployedByEmail);
        SetIfPresent(body, "diff_url", options.DiffUrl);

        return body;
    }

    private static void SetIfPresent(IDictionary<string, object?> body, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            body[key] = value;
        }
    }
}
