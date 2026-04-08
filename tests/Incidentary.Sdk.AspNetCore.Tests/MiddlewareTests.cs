using FluentAssertions;
using Incidentary.Sdk;
using Incidentary.Sdk.AspNetCore;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Incidentary.Sdk.AspNetCore.Tests;

public class MiddlewareTests
{
    [Fact]
    public async Task Middleware_RecordsHttpInEvent()
    {
        var mockClient = Substitute.For<IIncidentaryClient>();

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IIncidentaryClient>(mockClient);
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseIncidentary();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test", () => Results.Ok("hello"));
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/test");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        mockClient.Received(1).RecordRequestStart(CeKind.HttpIn);
        mockClient.Received(1).RecordRequest(200, Arg.Any<RecordRequestOptions>());
    }

    [Fact]
    public async Task Middleware_PropagatesTraceId()
    {
        var mockClient = Substitute.For<IIncidentaryClient>();
        var testTraceId = "test-trace-id-123";

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IIncidentaryClient>(mockClient);
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseIncidentary();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test", () => Results.Ok("hello"));
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("x-incidentary-trace-id", testTraceId);

        var response = await client.SendAsync(request);

        mockClient.Received(1).RecordRequest(
            200,
            Arg.Is<RecordRequestOptions>(o => o.TraceId == testTraceId));
    }

    [Fact]
    public async Task Middleware_Returns500OnException()
    {
        var mockClient = Substitute.For<IIncidentaryClient>();

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IIncidentaryClient>(mockClient);
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseIncidentary();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/error", IResult () => throw new InvalidOperationException("boom"));
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        var act = () => client.GetAsync("/error");
        // The test server throws, but the middleware should have recorded 500
        try { await act(); } catch { /* expected */ }

        mockClient.Received(1).RecordRequest(500, Arg.Any<RecordRequestOptions>());
    }
}
