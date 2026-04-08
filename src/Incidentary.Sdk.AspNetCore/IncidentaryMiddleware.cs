using System.Diagnostics;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Incidentary.Sdk.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that instruments inbound HTTP requests.
/// Records HTTP_IN events and propagates trace context.
/// </summary>
public sealed class IncidentaryMiddleware
{
    private readonly RequestDelegate _next;

    public IncidentaryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes an HTTP request through the Incidentary instrumentation pipeline.
    /// Reads <c>x-incidentary-trace-id</c> and <c>x-incidentary-parent-ce</c> headers for context propagation.
    /// Writes <c>x-incidentary-trace-id</c> on the response. Records an HTTP_IN causal event with
    /// status code, duration, method, route template, and body sizes.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IIncidentaryClient client)
    {
        // Extract or generate trace context
        var traceId = context.Request.Headers[HeaderConstants.TraceIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        var parentCeId = context.Request.Headers[HeaderConstants.ParentCeHeader].FirstOrDefault();
        var ceId = Guid.NewGuid().ToString();

        var traceContext = new TraceContext(traceId, ceId);

        // Set ambient context
        using var scope = IncidentaryActivity.SetContext(traceContext);

        // Set response headers for downstream correlation
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderConstants.TraceIdHeader] = traceId;
            return Task.CompletedTask;
        });

        client.RecordRequestStart(CeKind.HttpIn);
        var sw = Stopwatch.StartNew();
        int statusCode = 500; // Default if exception

        try
        {
            await _next(context).ConfigureAwait(false);
            statusCode = context.Response.StatusCode;
        }
        catch
        {
            statusCode = 500;
            throw;
        }
        finally
        {
            sw.Stop();
            var durationNs = sw.Elapsed.Ticks * 100; // Ticks are 100ns each

            // Try to get route template
            var endpoint = context.GetEndpoint();
            var routeTemplate = (endpoint as RouteEndpoint)?.RoutePattern.RawText;

            var options = new RecordRequestOptions
            {
                Kind = CeKind.HttpIn,
                EventType = EventTypes.HttpIn,
                DurationNs = durationNs,
                TraceId = traceId,
                ParentCeId = parentCeId,
                Method = context.Request.Method,
                RouteTemplate = routeTemplate,
                RequestBytes = context.Request.ContentLength,
                ResponseBytes = context.Response.ContentLength
            };

            client.RecordRequest(statusCode, options);
        }
    }
}
