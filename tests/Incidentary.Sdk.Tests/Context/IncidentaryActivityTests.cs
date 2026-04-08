using FluentAssertions;
using Incidentary.Sdk.Context;
using Xunit;

namespace Incidentary.Sdk.Tests.Context;

public sealed class IncidentaryActivityTests
{
    [Fact]
    public void Current_InitiallyNull()
    {
        IncidentaryActivity.Current.Should().BeNull();
    }

    [Fact]
    public void SetContext_MakesCurrent()
    {
        var ctx = TraceContext.NewRoot();

        using (IncidentaryActivity.SetContext(ctx))
        {
            IncidentaryActivity.Current.Should().Be(ctx);
        }
    }

    [Fact]
    public void SetContext_RestoresOnDispose()
    {
        var ctx = TraceContext.NewRoot();

        using (IncidentaryActivity.SetContext(ctx))
        {
            IncidentaryActivity.Current.Should().Be(ctx);
        }

        IncidentaryActivity.Current.Should().BeNull();
    }

    [Fact]
    public void NestedScopes_RestoreCorrectly()
    {
        var ctxA = TraceContext.NewRoot();
        var ctxB = TraceContext.NewRoot();

        using (IncidentaryActivity.SetContext(ctxA))
        {
            IncidentaryActivity.Current.Should().Be(ctxA);

            using (IncidentaryActivity.SetContext(ctxB))
            {
                IncidentaryActivity.Current.Should().Be(ctxB);
            }

            IncidentaryActivity.Current.Should().Be(ctxA);
        }

        IncidentaryActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task AsyncLocal_PropagatesAcrossAwait()
    {
        var ctx = TraceContext.NewRoot();

        using (IncidentaryActivity.SetContext(ctx))
        {
            await Task.Yield();
            IncidentaryActivity.Current.Should().Be(ctx);
        }
    }

    [Fact]
    public async Task AsyncLocal_IsolatesBetweenTasks()
    {
        var ctx = TraceContext.NewRoot();

        using (IncidentaryActivity.SetContext(ctx))
        {
            TraceContext? capturedInOtherTask = null;

            await Task.Run(() =>
            {
                // AsyncLocal copies the value into child tasks,
                // but we can verify isolation by setting a new value
                // in the child and checking it doesn't affect parent.
                capturedInOtherTask = IncidentaryActivity.Current;
            });

            // The child task inherits the value (AsyncLocal copy-on-read),
            // so it should see the parent's context.
            capturedInOtherTask.Should().Be(ctx);

            // But modifying in a parallel task should not affect the parent.
            var childCtx = TraceContext.NewRoot();

            await Task.Run(() =>
            {
                using (IncidentaryActivity.SetContext(childCtx))
                {
                    IncidentaryActivity.Current.Should().Be(childCtx);
                }
            });

            // Parent context should be unaffected.
            IncidentaryActivity.Current.Should().Be(ctx);
        }
    }

    [Fact]
    public void GetCurrentIds_WithContext()
    {
        var ctx = new TraceContext("trace-abc", "ce-xyz");

        using (IncidentaryActivity.SetContext(ctx))
        {
            var (traceId, ceId) = IncidentaryActivity.GetCurrentIds();

            traceId.Should().Be("trace-abc");
            ceId.Should().Be("ce-xyz");
        }
    }

    [Fact]
    public void GetCurrentIds_WithoutContext()
    {
        var (traceId, ceId) = IncidentaryActivity.GetCurrentIds();

        traceId.Should().BeNull();
        ceId.Should().BeNull();
    }

    [Fact]
    public void ContextScope_DoubleDispose_IsIdempotent()
    {
        // Covers IncidentaryActivity/ContextScope line 43 false branch:
        // `if (_disposed)` — on the SECOND Dispose() call, _disposed is true → returns immediately.
        var ctx = TraceContext.NewRoot();
        var scope = IncidentaryActivity.SetContext(ctx);

        scope.Dispose(); // First dispose: restores previous context, sets _disposed = true
        scope.Dispose(); // Second dispose: _disposed=true → early return (covers false branch)

        // After second dispose, context should still be null (no double-restore happened)
        IncidentaryActivity.Current.Should().BeNull();
    }
}
