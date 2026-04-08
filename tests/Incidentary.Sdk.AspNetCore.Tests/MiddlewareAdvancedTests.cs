using FluentAssertions;
using Incidentary.Sdk;
using Incidentary.Sdk.AspNetCore;
using Incidentary.Sdk.WireFormat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Net;
using System.Text;
using Xunit;

namespace Incidentary.Sdk.AspNetCore.Tests;

/// <summary>
/// Extended middleware tests covering status code variants, route templates,
/// body sizes, header propagation, and concurrent traffic.
/// </summary>
public sealed class MiddlewareAdvancedTests
{
    private static async Task<(IHost host, HttpClient client, IIncidentaryClient mockClient)>
        BuildTestHostAsync(Action<IEndpointRouteBuilder>? endpoints = null)
    {
        var mockClient = Substitute.For<IIncidentaryClient>();

        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddSingleton<IIncidentaryClient>(mockClient);
                    s.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseIncidentary();
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        endpoints?.Invoke(e);
                        e.MapGet("/ok", () => Results.Ok("ok"));
                        e.MapGet("/not-found", () => Results.NotFound());
                        e.MapGet("/bad-request", () => Results.BadRequest("bad"));
                        e.MapGet("/unauthorized", () => Results.Unauthorized());
                        e.MapGet("/conflict", () => Results.Conflict());
                        e.MapGet("/error", IResult () => throw new InvalidOperationException("boom"));
                        e.MapPost("/echo", async (HttpContext ctx) =>
                        {
                            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                            return Results.Ok(body);
                        });
                    });
                });
            })
            .StartAsync();

        return (host, host.GetTestClient(), mockClient);
    }

    // ── 2xx status codes ───────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_Records200_OnOkResponse()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/ok");

            mockClient.Received(1).RecordRequest(200, Arg.Any<RecordRequestOptions?>());
        }
    }

    // ── 4xx status codes ──────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_Records404_OnNotFound()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/not-found");

            mockClient.Received(1).RecordRequest(404, Arg.Any<RecordRequestOptions?>());
        }
    }

    [Fact]
    public async Task Middleware_Records400_OnBadRequest()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/bad-request");

            mockClient.Received(1).RecordRequest(400, Arg.Any<RecordRequestOptions?>());
        }
    }

    [Fact]
    public async Task Middleware_Records401_OnUnauthorized()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/unauthorized");

            mockClient.Received(1).RecordRequest(401, Arg.Any<RecordRequestOptions?>());
        }
    }

    [Fact]
    public async Task Middleware_Records409_OnConflict()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/conflict");

            mockClient.Received(1).RecordRequest(409, Arg.Any<RecordRequestOptions?>());
        }
    }

    // ── 5xx status codes ──────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_Records500_OnUnhandledException()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            try { await client.GetAsync("/error"); } catch { /* expected */ }

            mockClient.Received(1).RecordRequest(500, Arg.Any<RecordRequestOptions?>());
        }
    }

    // ── RecordRequestStart always called ─────────────────────────────────

    [Fact]
    public async Task Middleware_AlwaysCallsRecordRequestStart()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/ok");

            mockClient.Received(1).RecordRequestStart(CeKind.HttpIn);
        }
    }

    // ── Trace ID propagation ──────────────────────────────────────────────

    [Fact]
    public async Task Middleware_WithIncomingTraceId_PropagatesInRecordRequest()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
            request.Headers.Add("x-incidentary-trace-id", "my-trace-id-123");

            await client.SendAsync(request);

            mockClient.Received(1).RecordRequest(
                200,
                Arg.Is<RecordRequestOptions>(o => o.TraceId == "my-trace-id-123"));
        }
    }

    [Fact]
    public async Task Middleware_WithoutIncomingTraceId_GeneratesNewTraceId()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            await client.GetAsync("/ok");

            mockClient.Received(1).RecordRequest(
                200,
                Arg.Is<RecordRequestOptions>(o =>
                    o.TraceId != null && o.TraceId.Length > 0));
        }
    }

    [Fact]
    public async Task Middleware_WithIncomingParentCeId_PropagatesParent()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
            request.Headers.Add("x-incidentary-trace-id", "t-abc");
            request.Headers.Add("x-incidentary-parent-ce", "parent-ce-xyz");

            await client.SendAsync(request);

            mockClient.Received(1).RecordRequest(
                200,
                Arg.Is<RecordRequestOptions>(o => o.ParentCeId == "parent-ce-xyz"));
        }
    }

    // ── Request/response body size ─────────────────────────────────────────

    [Fact]
    public async Task Middleware_Post_RecordsNonZeroRequestBytes()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            RecordRequestOptions? captured = null;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(ci => captured = ci.ArgAt<RecordRequestOptions?>(1));

            var content = new StringContent("hello world", Encoding.UTF8, "text/plain");
            await client.PostAsync("/echo", content);

            captured.Should().NotBeNull();
            captured!.RequestBytes.Should().BeGreaterThan(0);
        }
    }

    // ── Duration capture ──────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_RecordsDuration_GreaterThanZero()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            RecordRequestOptions? captured = null;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(ci => captured = ci.ArgAt<RecordRequestOptions?>(1));

            await client.GetAsync("/ok");

            captured.Should().NotBeNull();
            captured!.DurationNs.Should().BeGreaterThan(0);
        }
    }

    // ── HTTP method capture ────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_RecordsHttpMethod_GET()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            RecordRequestOptions? captured = null;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(ci => captured = ci.ArgAt<RecordRequestOptions?>(1));

            await client.GetAsync("/ok");

            captured.Should().NotBeNull();
            captured!.Method.Should().Be("GET");
        }
    }

    [Fact]
    public async Task Middleware_RecordsHttpMethod_POST()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            RecordRequestOptions? captured = null;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(ci => captured = ci.ArgAt<RecordRequestOptions?>(1));

            await client.PostAsync("/echo", new StringContent("{}"));

            captured.Should().NotBeNull();
            captured!.Method.Should().Be("POST");
        }
    }

    // ── Kind ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_UsesHttpInKind_ForIncomingRequests()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            RecordRequestOptions? captured = null;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(ci => captured = ci.ArgAt<RecordRequestOptions?>(1));

            await client.GetAsync("/ok");

            captured.Should().NotBeNull();
            captured!.Kind.Should().Be(CeKind.HttpIn);
        }
    }

    // ── Concurrent traffic ─────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_ConcurrentRequests_AllRecorded()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            const int count = 20;
            var tasks = Enumerable.Range(0, count)
                .Select(_ => client.GetAsync("/ok"))
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            responses.Should().AllSatisfy(r =>
                r.StatusCode.Should().Be(HttpStatusCode.OK));

            // RecordRequest should be called once per request
            mockClient.Received(count).RecordRequest(200, Arg.Any<RecordRequestOptions?>());
        }
    }

    // ── Exception path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Middleware_Exception_StillRecordsRequest()
    {
        var (host, client, mockClient) = await BuildTestHostAsync();
        using (host) using (client)
        {
            var recorded = false;
            mockClient.When(m => m.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
                .Do(_ => recorded = true);

            try { await client.GetAsync("/error"); } catch { /* ignored */ }

            recorded.Should().BeTrue("middleware must record even on exceptions");
        }
    }
}
