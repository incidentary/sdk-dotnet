using System.Text.RegularExpressions;

namespace Incidentary.Sdk.DownstreamEdge;

/// <summary>
/// Resolves a stable edge key from outbound call metadata, using a 5-level quality hierarchy.
/// </summary>
public static partial class DownstreamEdgeKeyResolver
{
    /// <summary>
    /// Resolves an edge key from the provided metadata and/or URL.
    /// </summary>
    /// <param name="metadata">Optional metadata about the outbound call.</param>
    /// <param name="url">Optional URL of the outbound call.</param>
    /// <returns>The resolved edge key and its quality level.</returns>
    public static EdgeKeyResolution Resolve(OutboundRetryMetadata? metadata, Uri? url)
    {
        if (metadata is not null)
        {
            // Level 1: Explicit identifiers (priority order)
            if (HasValue(metadata.RetryGroupId))
            {
                return new EdgeKeyResolution { Key = metadata.RetryGroupId!, Quality = RetryKeyQualities.Explicit };
            }

            if (HasValue(metadata.IdempotencyKey))
            {
                return new EdgeKeyResolution { Key = metadata.IdempotencyKey!, Quality = RetryKeyQualities.Explicit };
            }

            if (HasValue(metadata.OperationKey))
            {
                return new EdgeKeyResolution { Key = metadata.OperationKey!, Quality = RetryKeyQualities.Explicit };
            }

            if (HasValue(metadata.RetryKey))
            {
                return new EdgeKeyResolution { Key = metadata.RetryKey!, Quality = RetryKeyQualities.Explicit };
            }

            // Level 2: Route template
            if (HasValue(metadata.RouteTemplate))
            {
                return new EdgeKeyResolution { Key = metadata.RouteTemplate!, Quality = RetryKeyQualities.RouteTemplate };
            }

            if (HasValue(metadata.RouteKey))
            {
                return new EdgeKeyResolution { Key = metadata.RouteKey!, Quality = RetryKeyQualities.RouteTemplate };
            }

            // Level 3: Logical edge
            if (HasValue(metadata.DownstreamService) || HasValue(metadata.EdgeKey))
            {
                var service = metadata.DownstreamService ?? metadata.EdgeKey!;
                var operation = metadata.OperationName ?? "unknown";
                return new EdgeKeyResolution { Key = $"{service}:{operation}", Quality = RetryKeyQualities.LogicalEdge };
            }
        }

        // Level 4: Normalized URL
        if (url is not null)
        {
            var normalized = NormalizeUrl(url);
            return new EdgeKeyResolution { Key = normalized, Quality = RetryKeyQualities.NormalizedUrl };
        }

        // Level 5: Unknown
        return new EdgeKeyResolution { Key = "unknown", Quality = RetryKeyQualities.Unknown };
    }

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static string NormalizeUrl(Uri url)
    {
        // Take scheme://host/path, strip query and fragment
        var basePath = $"{url.Scheme}://{url.Authority}{url.AbsolutePath}";

        // Remove trailing slash (unless it's just "/")
        if (basePath.Length > 1 && basePath.EndsWith('/'))
        {
            basePath = basePath[..^1];
        }

        // Replace path segments that look like UUIDs or pure numbers with :id
        var segments = basePath.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (UuidPattern().IsMatch(segments[i]) || NumericPattern().IsMatch(segments[i]))
            {
                segments[i] = ":id";
            }
        }

        return string.Join('/', segments);
    }

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumericPattern();
}
