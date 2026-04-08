namespace Incidentary.Sdk.Integrations;

/// <summary>
/// Defines a pluggable integration that instruments a specific library or framework.
/// Integrations are discovered and setup automatically by the client when auto-instrumentation is enabled.
/// </summary>
public interface IIntegration
{
    /// <summary>Unique name identifying this integration (e.g., "http", "grpc", "efcore").</summary>
    string Name { get; }

    /// <summary>Checks whether the target library is available at runtime (e.g., assembly loaded).</summary>
    bool IsAvailable();

    /// <summary>
    /// Sets up instrumentation. Returns a disposable that tears down instrumentation when disposed.
    /// Must not throw — failures should be logged and the integration skipped.
    /// </summary>
    IDisposable Setup(IIncidentaryClient client);
}
