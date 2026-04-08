namespace Incidentary.Sdk.Context;

/// <summary>Extracts and injects Incidentary trace context headers.</summary>
public static class HeaderPropagation
{
    /// <summary>Extracts trace context from HTTP request headers.</summary>
    public static TraceContext? Extract(IDictionary<string, string> headers)
    {
        return Extract(key => headers.TryGetValue(key, out var value) ? value : null);
    }

    /// <summary>
    /// Extracts trace context from a header value lookup function
    /// (for adapting various header collections).
    /// </summary>
    public static TraceContext? Extract(Func<string, string?> headerGetter)
    {
        var traceId = headerGetter(HeaderConstants.TraceIdHeader);

        if (string.IsNullOrWhiteSpace(traceId))
        {
            return null;
        }

        var ceId = headerGetter(HeaderConstants.ParentCeHeader);

        return new TraceContext(
            traceId,
            string.IsNullOrWhiteSpace(ceId) ? Guid.NewGuid().ToString() : ceId);
    }

    /// <summary>Injects trace context into HTTP request headers.</summary>
    public static void Inject(IDictionary<string, string> headers, TraceContext context)
    {
        headers[HeaderConstants.TraceIdHeader] = context.TraceId;
        headers[HeaderConstants.ParentCeHeader] = context.CeId;
    }

    /// <summary>Injects trace context via header setter function.</summary>
    public static void Inject(Action<string, string> headerSetter, TraceContext context)
    {
        headerSetter(HeaderConstants.TraceIdHeader, context.TraceId);
        headerSetter(HeaderConstants.ParentCeHeader, context.CeId);
    }
}
