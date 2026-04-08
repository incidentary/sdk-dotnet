using FluentAssertions;
using Incidentary.Sdk.Transport;
using Xunit;

namespace Incidentary.Sdk.Tests.Transport;

public sealed class CircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new CircuitBreaker();

        cb.IsOpen.Should().BeFalse();
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void SingleFailure_StaysClosed()
    {
        var cb = new CircuitBreaker(maxFailures: 3);

        cb.RecordFailure();

        cb.IsOpen.Should().BeFalse();
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void ThreeFailures_OpensCircuit()
    {
        var cb = new CircuitBreaker(maxFailures: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeTrue();
        cb.AllowRequest().Should().BeFalse();
    }

    [Fact]
    public void AfterCooldown_AllowsRequest()
    {
        var currentTick = 1000L;
        var cb = new CircuitBreaker(
            maxFailures: 3,
            cooldownMs: 500,
            tickProvider: () => currentTick);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeTrue();
        cb.AllowRequest().Should().BeFalse();

        // Advance past cooldown
        currentTick = 1501;

        // Half-open: should allow one request
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void SuccessAfterFailures_ResetsCounter()
    {
        var cb = new CircuitBreaker(maxFailures: 3);

        // Two failures
        cb.RecordFailure();
        cb.RecordFailure();

        // A success resets the counter
        cb.RecordSuccess();

        // Two more failures should NOT open the circuit (counter was reset)
        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeFalse();
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void SuccessAfterHalfOpen_ClosesCircuit()
    {
        var currentTick = 1000L;
        var cb = new CircuitBreaker(
            maxFailures: 3,
            cooldownMs: 500,
            tickProvider: () => currentTick);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeTrue();

        // Advance past cooldown (half-open)
        currentTick = 1501;
        cb.AllowRequest().Should().BeTrue();

        // Success closes the circuit
        cb.RecordSuccess();
        cb.IsOpen.Should().BeFalse();
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentFailures_ThreadSafe()
    {
        var cb = new CircuitBreaker(maxFailures: 100);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => cb.RecordFailure()))
            .ToArray();

        await Task.WhenAll(tasks);

        cb.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void FailureDuringHalfOpen_ReopensCircuit()
    {
        var currentTick = 1000L;
        var cb = new CircuitBreaker(
            maxFailures: 3,
            cooldownMs: 500,
            tickProvider: () => currentTick);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        // Advance past cooldown (half-open)
        currentTick = 1501;
        cb.AllowRequest().Should().BeTrue();

        // Failure during half-open immediately re-opens
        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();
        cb.AllowRequest().Should().BeFalse();
    }
}
