using FluentAssertions;
using Incidentary.Sdk.DownstreamEdge;
using Xunit;

namespace Incidentary.Sdk.Tests.DownstreamEdge;

public sealed class DownstreamEdgeKeyResolverTests
{
    [Fact]
    public void Resolve_WithRetryGroupId_ReturnsExplicit()
    {
        var metadata = new OutboundRetryMetadata { RetryGroupId = "grp-123" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("grp-123");
        result.Quality.Should().Be(RetryKeyQualities.Explicit);
    }

    [Fact]
    public void Resolve_WithIdempotencyKey_ReturnsExplicit()
    {
        var metadata = new OutboundRetryMetadata { IdempotencyKey = "idem-456" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("idem-456");
        result.Quality.Should().Be(RetryKeyQualities.Explicit);
    }

    [Fact]
    public void Resolve_WithOperationKey_ReturnsExplicit()
    {
        var metadata = new OutboundRetryMetadata { OperationKey = "op-789" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("op-789");
        result.Quality.Should().Be(RetryKeyQualities.Explicit);
    }

    [Fact]
    public void Resolve_ExplicitPriority_RetryGroupIdWins()
    {
        var metadata = new OutboundRetryMetadata
        {
            RetryGroupId = "grp-first",
            IdempotencyKey = "idem-second",
        };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("grp-first");
        result.Quality.Should().Be(RetryKeyQualities.Explicit);
    }

    [Fact]
    public void Resolve_WithRouteTemplate_ReturnsRouteTemplate()
    {
        var metadata = new OutboundRetryMetadata { RouteTemplate = "/orders/{id}/items" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("/orders/{id}/items");
        result.Quality.Should().Be(RetryKeyQualities.RouteTemplate);
    }

    [Fact]
    public void Resolve_WithRouteKey_ReturnsRouteTemplate()
    {
        var metadata = new OutboundRetryMetadata { RouteKey = "orders-list" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("orders-list");
        result.Quality.Should().Be(RetryKeyQualities.RouteTemplate);
    }

    [Fact]
    public void Resolve_WithDownstreamService_ReturnsLogicalEdge()
    {
        var metadata = new OutboundRetryMetadata
        {
            DownstreamService = "payments",
            OperationName = "charge",
        };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("payments:charge");
        result.Quality.Should().Be(RetryKeyQualities.LogicalEdge);
    }

    [Fact]
    public void Resolve_WithDownstreamServiceNoOp_UsesUnknownOp()
    {
        var metadata = new OutboundRetryMetadata { DownstreamService = "payments" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("payments:unknown");
        result.Quality.Should().Be(RetryKeyQualities.LogicalEdge);
    }

    [Fact]
    public void Resolve_WithUrl_NormalizesAndReturnsUrl()
    {
        var url = new Uri("https://api.example.com/orders/123/items");

        var result = DownstreamEdgeKeyResolver.Resolve(null, url);

        result.Key.Should().Be("https://api.example.com/orders/:id/items");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    [Fact]
    public void Resolve_WithUrlContainingUuid_ReplacesWithId()
    {
        var url = new Uri("https://api.example.com/users/550e8400-e29b-41d4-a716-446655440000/profile");

        var result = DownstreamEdgeKeyResolver.Resolve(null, url);

        result.Key.Should().Be("https://api.example.com/users/:id/profile");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    [Fact]
    public void Resolve_WithUrlContainingNumericSegment_ReplacesWithId()
    {
        var url = new Uri("https://api.example.com/orders/42/items/99");

        var result = DownstreamEdgeKeyResolver.Resolve(null, url);

        result.Key.Should().Be("https://api.example.com/orders/:id/items/:id");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    [Fact]
    public void Resolve_WithUrl_StripsQueryAndFragment()
    {
        var url = new Uri("https://api.com/path?q=1#frag");

        var result = DownstreamEdgeKeyResolver.Resolve(null, url);

        result.Key.Should().Be("https://api.com/path");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    [Fact]
    public void Resolve_NullMetadataAndUrl_ReturnsUnknown()
    {
        var result = DownstreamEdgeKeyResolver.Resolve(null, null);

        result.Key.Should().Be("unknown");
        result.Quality.Should().Be(RetryKeyQualities.Unknown);
    }

    [Fact]
    public void Resolve_EmptyMetadata_WithUrl_FallsToUrl()
    {
        var metadata = new OutboundRetryMetadata();
        var url = new Uri("https://api.example.com/health");

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, url);

        result.Key.Should().Be("https://api.example.com/health");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    [Fact]
    public void Resolve_EmptyStringFields_Ignored()
    {
        var metadata = new OutboundRetryMetadata
        {
            RetryGroupId = "",
            IdempotencyKey = "   ",
            RouteTemplate = "",
            DownstreamService = "",
        };
        var url = new Uri("https://api.example.com/test");

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, url);

        result.Key.Should().Be("https://api.example.com/test");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

    // ── Branch coverage for lower-priority keys (lines 36, 55, 81) ────────────

    [Fact]
    public void Resolve_WithRetryKey_ReturnsExplicit()
    {
        // Covers line 36: `if (HasValue(metadata.RetryKey))` — the true branch.
        // All higher-priority keys are null; only RetryKey is set.
        var metadata = new OutboundRetryMetadata { RetryKey = "retry-abc" };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("retry-abc");
        result.Quality.Should().Be(RetryKeyQualities.Explicit);
    }

    [Fact]
    public void Resolve_WithEdgeKeyOnly_ReturnsLogicalEdge()
    {
        // Covers line 55 false+true branches: DownstreamService is null, EdgeKey is set.
        // `HasValue(metadata.DownstreamService) || HasValue(metadata.EdgeKey)` →
        //   first operand false, second operand true.
        var metadata = new OutboundRetryMetadata
        {
            EdgeKey = "edge-svc",
            OperationName = "do-it",
        };

        var result = DownstreamEdgeKeyResolver.Resolve(metadata, null);

        result.Key.Should().Be("edge-svc:do-it");
        result.Quality.Should().Be(RetryKeyQualities.LogicalEdge);
    }

    [Fact]
    public void Resolve_WithUrlEndingInSlash_StripsTrailingSlash()
    {
        // Covers line 81: `if (basePath.Length > 1 && basePath.EndsWith('/'))` true branch.
        var url = new Uri("https://api.example.com/orders/");

        var result = DownstreamEdgeKeyResolver.Resolve(null, url);

        // Trailing slash should be stripped
        result.Key.Should().Be("https://api.example.com/orders");
        result.Quality.Should().Be(RetryKeyQualities.NormalizedUrl);
    }

}
