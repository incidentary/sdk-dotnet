using FluentAssertions;
using Incidentary.Sdk.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Incidentary.Sdk.Tests.Integrations;

public class IntegrationRegistryTests
{
    [Fact]
    public void Register_AddsToRegisteredList()
    {
        using var registry = new IntegrationRegistry();
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");

        registry.Register(integration);

        registry.GetRegistered().Should().Contain("test");
    }

    [Fact]
    public void DiscoverAndSetup_AvailableIntegration_SetsUp()
    {
        using var registry = new IntegrationRegistry();
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(Substitute.For<IDisposable>());
        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        registry.GetActive().Should().Contain("test");
    }

    [Fact]
    public void DiscoverAndSetup_UnavailableIntegration_Skips()
    {
        using var registry = new IntegrationRegistry();
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("unavailable");
        integration.IsAvailable().Returns(false);
        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        registry.GetActive().Should().BeEmpty();
    }

    [Fact]
    public void DiscoverAndSetup_FailingIntegration_DoesNotAbortOthers()
    {
        using var registry = new IntegrationRegistry();

        var failing = Substitute.For<IIntegration>();
        failing.Name.Returns("failing");
        failing.IsAvailable().Returns(true);
        failing.Setup(Arg.Any<IIncidentaryClient>()).Returns(_ => throw new InvalidOperationException("boom"));

        var working = Substitute.For<IIntegration>();
        working.Name.Returns("working");
        working.IsAvailable().Returns(true);
        working.Setup(Arg.Any<IIncidentaryClient>()).Returns(Substitute.For<IDisposable>());

        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(failing);
        registry.Register(working);
        registry.DiscoverAndSetup(client);

        registry.GetActive().Should().Contain("working").And.NotContain("failing");
    }

    [Fact]
    public void Dispose_CallsCleanupOnActiveIntegrations()
    {
        var cleanup = Substitute.For<IDisposable>();
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(cleanup);

        var registry = new IntegrationRegistry();
        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        registry.Dispose();

        cleanup.Received(1).Dispose();
    }

    [Fact]
    public void Register_NullIntegration_ThrowsArgumentNull()
    {
        using var registry = new IntegrationRegistry();
        var act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DiscoverAndSetup_SetupThrows_InvokesErrorCallback()
    {
        var boom = new InvalidOperationException("setup failed");
        List<Exception> captured = [];

        using var registry = new IntegrationRegistry(onError: ex => captured.Add(ex));

        var failing = Substitute.For<IIntegration>();
        failing.Name.Returns("failing");
        failing.IsAvailable().Returns(true);
        failing.Setup(Arg.Any<IIncidentaryClient>()).Returns(_ => throw boom);

        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(failing);
        registry.DiscoverAndSetup(client);

        captured.Should().ContainSingle().Which.Should().BeSameAs(boom);
    }

    [Fact]
    public void DiscoverAndSetup_SetupThrows_ErrorCallbackThrows_DoesNotPropagate()
    {
        // The registry wraps the callback in try/catch — a throwing callback must not escape
        using var registry = new IntegrationRegistry(onError: _ => throw new InvalidOperationException("callback boom"));

        var failing = Substitute.For<IIntegration>();
        failing.Name.Returns("failing");
        failing.IsAvailable().Returns(true);
        failing.Setup(Arg.Any<IIncidentaryClient>()).Returns(_ => throw new InvalidOperationException("setup"));

        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(failing);

        var act = () => registry.DiscoverAndSetup(client);

        act.Should().NotThrow("callback errors must be swallowed");
    }

    [Fact]
    public void Dispose_CleanupThrows_DoesNotPropagate()
    {
        var cleanup = Substitute.For<IDisposable>();
        cleanup.When(c => c.Dispose()).Throw<InvalidOperationException>();

        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(cleanup);

        var registry = new IntegrationRegistry();
        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        var act = () => registry.Dispose();

        act.Should().NotThrow("teardown exceptions must be swallowed");
    }

    // ── Logger branch coverage ────────────────────────────────────────────────
    // IntegrationRegistry has `if (_logger is not null) Log...()` guards on every
    // log call. Tests below pass NullLogger.Instance to cover the logger-present path.

    [Fact]
    public void WithLogger_UnavailableIntegration_LogsNotAvailable()
    {
        using var registry = new IntegrationRegistry(logger: NullLogger.Instance);
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("unavailable");
        integration.IsAvailable().Returns(false);
        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(integration);
        // Must not throw; NullLogger.Instance absorbs the log call
        var act = () => registry.DiscoverAndSetup(client);

        act.Should().NotThrow();
        registry.GetActive().Should().BeEmpty();
    }

    [Fact]
    public void WithLogger_AvailableIntegration_LogsSetupComplete()
    {
        using var registry = new IntegrationRegistry(logger: NullLogger.Instance);
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("working");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(Substitute.For<IDisposable>());
        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(integration);
        var act = () => registry.DiscoverAndSetup(client);

        act.Should().NotThrow();
        registry.GetActive().Should().Contain("working");
    }

    [Fact]
    public void WithLogger_SetupThrows_LogsSetupFailed()
    {
        using var registry = new IntegrationRegistry(logger: NullLogger.Instance);
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("failing");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(_ => throw new InvalidOperationException("boom"));
        var client = Substitute.For<IIncidentaryClient>();

        registry.Register(integration);
        var act = () => registry.DiscoverAndSetup(client);

        act.Should().NotThrow();
        registry.GetActive().Should().BeEmpty();
    }

    [Fact]
    public void WithLogger_CleanupThrows_LogsTeardownFailed()
    {
        var cleanup = Substitute.For<IDisposable>();
        cleanup.When(c => c.Dispose()).Throw<InvalidOperationException>();

        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(cleanup);

        var registry = new IntegrationRegistry(logger: NullLogger.Instance);
        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        var act = () => registry.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var cleanup = Substitute.For<IDisposable>();
        var integration = Substitute.For<IIntegration>();
        integration.Name.Returns("test");
        integration.IsAvailable().Returns(true);
        integration.Setup(Arg.Any<IIncidentaryClient>()).Returns(cleanup);

        var registry = new IntegrationRegistry();
        var client = Substitute.For<IIncidentaryClient>();
        registry.Register(integration);
        registry.DiscoverAndSetup(client);

        registry.Dispose();
        registry.Dispose(); // second call must be a no-op

        cleanup.Received(1).Dispose(); // cleanup called exactly once
    }
}
