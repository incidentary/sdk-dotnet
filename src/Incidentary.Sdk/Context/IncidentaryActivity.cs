namespace Incidentary.Sdk.Context;

/// <summary>Manages ambient trace context via AsyncLocal for propagation across async boundaries.</summary>
public static class IncidentaryActivity
{
    private static readonly AsyncLocal<TraceContext?> CurrentContext = new();

    /// <summary>Gets the current trace context, or null if none is set.</summary>
    public static TraceContext? Current => CurrentContext.Value;

    /// <summary>
    /// Sets the current trace context and returns a disposable scope
    /// that restores the previous value on dispose.
    /// </summary>
    public static IDisposable SetContext(TraceContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new ContextScope(previous);
    }

    /// <summary>Gets current trace ID and CE ID, or (null, null) if no context.</summary>
    public static (string? TraceId, string? CeId) GetCurrentIds()
    {
        var ctx = CurrentContext.Value;
        return ctx.HasValue
            ? (ctx.Value.TraceId, ctx.Value.CeId)
            : (null, null);
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly TraceContext? _previous;
        private bool _disposed;

        public ContextScope(TraceContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentContext.Value = _previous;
        }
    }
}
