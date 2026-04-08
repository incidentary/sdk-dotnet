using FluentAssertions;
using Incidentary.Sdk;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.Extensions.Http;
using Incidentary.Sdk.WireFormat;
using NSubstitute;
using System.Net;
using Xunit;

namespace Incidentary.Sdk.AspNetCore.Tests.Http;

public sealed class IncidentaryDelegatingHandlerTests
{
    private readonly IIncidentaryClient _client = Substitute.For<IIncidentaryClient>();

    private HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? innerHandler = null)
    {
        innerHandler ??= (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new IncidentaryDelegatingHandler(_client)
        {
            InnerHandler = new FuncHandler(innerHandler)
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
    }

    // ── Constructor guard ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        var act = () => new IncidentaryDelegatingHandler(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("client");
    }

    // ── Header injection ───────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_InjectsTraceIdHeader()
    {
        HttpRequestMessage? captured = null;

        using var http = CreateHttpClient((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        await http.GetAsync("/health");

        captured.Should().NotBeNull();
        captured!.Headers.Contains(HeaderConstants.TraceIdHeader).Should().BeTrue();
        var traceId = captured.Headers.GetValues(HeaderConstants.TraceIdHeader).Single();
        traceId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(traceId, out _).Should().BeTrue("trace IDs should be GUIDs");
    }

    [Fact]
    public async Task SendAsync_InjectsParentCeIdHeader()
    {
        HttpRequestMessage? captured = null;

        using var http = CreateHttpClient((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        await http.GetAsync("/health");

        captured.Should().NotBeNull();
        captured!.Headers.Contains(HeaderConstants.ParentCeHeader).Should().BeTrue();
        var parentCeId = captured.Headers.GetValues(HeaderConstants.ParentCeHeader).Single();
        parentCeId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(parentCeId, out _).Should().BeTrue("parent CE IDs should be GUIDs");
    }

    [Fact]
    public async Task SendAsync_WithAmbientContext_PropagatesTraceId()
    {
        var ctx = new TraceContext("ambient-trace-id", "ambient-ce-id");

        HttpRequestMessage? captured = null;
        using var http = CreateHttpClient((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using (IncidentaryActivity.SetContext(ctx))
        {
            await http.GetAsync("/resource");
        }

        captured.Should().NotBeNull();
        var injectedTraceId = captured!.Headers.GetValues(HeaderConstants.TraceIdHeader).Single();
        injectedTraceId.Should().Be("ambient-trace-id");
    }

    [Fact]
    public async Task SendAsync_ParentCeId_IsDistinctFromAmbientCeId()
    {
        // The handler generates a NEW outbound CE ID for the parent-ce header
        // (it's not the ambient CE ID — it's the ID of the outbound event itself)
        var ctx = new TraceContext("trace-xyz", "ambient-ce-abc");

        HttpRequestMessage? captured = null;
        using var http = CreateHttpClient((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using (IncidentaryActivity.SetContext(ctx))
        {
            await http.GetAsync("/downstream");
        }

        var injectedParentCe = captured!.Headers.GetValues(HeaderConstants.ParentCeHeader).Single();
        // The outbound CE ID propagated is a fresh GUID (not ambient-ce-abc)
        injectedParentCe.Should().NotBe("ambient-ce-abc");
        Guid.TryParse(injectedParentCe, out _).Should().BeTrue();
    }

    // ── Event recording ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_RecordsRequestStart_WithHttpOutKind()
    {
        using var http = CreateHttpClient();

        await http.GetAsync("/health");

        _client.Received(1).RecordRequestStart(CeKind.HttpOut);
    }

    [Fact]
    public async Task SendAsync_Success_RecordsHttpOutEvent()
    {
        using var http = CreateHttpClient(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await http.GetAsync("/health");

        _client.Received(1).RecordRequest(
            200,
            Arg.Is<RecordRequestOptions>(o =>
                o.Kind == CeKind.HttpOut &&
                o.EventType == EventTypes.HttpOut));
    }

    [Fact]
    public async Task SendAsync_Records_StatusCode_FromResponse()
    {
        using var http = CreateHttpClient(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));

        await http.PostAsync("/orders", new StringContent("{}"));

        _client.Received(1).RecordRequest(
            201,
            Arg.Any<RecordRequestOptions>());
    }

    [Fact]
    public async Task SendAsync_Records_HttpMethod_InOptions()
    {
        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        await http.DeleteAsync("/resource/1");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be("DELETE");
    }

    [Fact]
    public async Task SendAsync_Records_AbsolutePath_AsRouteTemplate()
    {
        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        await http.GetAsync("/api/users/42/orders");

        captured.Should().NotBeNull();
        captured!.RouteTemplate.Should().Be("/api/users/42/orders");
    }

    [Fact]
    public async Task SendAsync_Records_PositiveDuration()
    {
        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        await http.GetAsync("/health");

        captured.Should().NotBeNull();
        captured!.DurationNs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SendAsync_Records_OutboundCeId_InEventAttrs()
    {
        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        await http.GetAsync("/health");

        captured.Should().NotBeNull();
        captured!.EventAttrs.Should().ContainKey("outbound_ce_id");
        Guid.TryParse(captured.EventAttrs!["outbound_ce_id"].ToString(), out _)
            .Should().BeTrue();
    }

    // ── Error handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_NetworkFailure_Records502AndRethrows()
    {
        using var http = CreateHttpClient(
            (_, _) => throw new HttpRequestException("connection refused"));

        RecordRequestOptions? captured = null;
        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        var act = () => http.GetAsync("/health");

        await act.Should().ThrowAsync<HttpRequestException>();

        _client.Received(1).RecordRequest(502, Arg.Any<RecordRequestOptions>());
        captured.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_Cancellation_RecordsCancelledAndRethrows()
    {
        using var cts = new CancellationTokenSource();

        using var http = CreateHttpClient(async (_, token) =>
        {
            await Task.Delay(500, token); // Will be cancelled
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        RecordRequestOptions? captured = null;
        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        await cts.CancelAsync();

        var act = () => http.GetAsync("/slow", cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();

        _client.Received(1).RecordRequest(0, Arg.Is<RecordRequestOptions>(o => o.Cancelled));
        captured.Should().NotBeNull();
        captured!.Cancelled.Should().BeTrue();
        captured.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_TraceId_PropagatedInOptions()
    {
        var ctx = new TraceContext("trace-abc", "ce-xyz");

        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        using (IncidentaryActivity.SetContext(ctx))
        {
            await http.GetAsync("/resource");
        }

        captured.Should().NotBeNull();
        captured!.TraceId.Should().Be("trace-abc");
    }

    [Fact]
    public async Task SendAsync_ParentCeId_InOptions_IsAmbientCeId()
    {
        var ctx = new TraceContext("trace-id", "parent-ce-from-ambient");

        using var http = CreateHttpClient();
        RecordRequestOptions? captured = null;

        _client.When(c => c.RecordRequest(Arg.Any<int>(), Arg.Any<RecordRequestOptions?>()))
            .Do(callInfo => captured = callInfo.ArgAt<RecordRequestOptions?>(1));

        using (IncidentaryActivity.SetContext(ctx))
        {
            await http.GetAsync("/resource");
        }

        captured.Should().NotBeNull();
        // The parentCeId in the recorded options links back to the ambient (inbound) span
        captured!.ParentCeId.Should().Be("parent-ce-from-ambient");
    }

    // ── Test double ────────────────────────────────────────────────────────

    private sealed class FuncHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _func;

        public FuncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> func)
        {
            _func = func;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _func(request, cancellationToken);
    }
}
