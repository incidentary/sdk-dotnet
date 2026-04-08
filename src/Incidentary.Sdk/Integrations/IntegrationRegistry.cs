using Microsoft.Extensions.Logging;

namespace Incidentary.Sdk.Integrations;

/// <summary>
/// Manages the lifecycle of integration plugins: discovery, setup, and teardown.
/// Individual integration failures do not affect other integrations.
/// </summary>
public sealed partial class IntegrationRegistry : IDisposable
{
    private readonly List<IIntegration> _registered = [];
    private readonly List<(IIntegration Integration, IDisposable Cleanup)> _active = [];
    private readonly ILogger? _logger;
    private readonly Action<Exception>? _onError;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public IntegrationRegistry(ILogger? logger = null, Action<Exception>? onError = null)
    {
        _logger = logger;
        _onError = onError;
    }

    /// <summary>Registers an integration for later discovery and setup.</summary>
    public void Register(IIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        lock (_syncRoot)
        {
            _registered.Add(integration);
        }
    }

    /// <summary>Discovers available integrations and sets them up.</summary>
    public void DiscoverAndSetup(IIncidentaryClient client)
    {
        lock (_syncRoot)
        {
            foreach (var integration in _registered)
            {
                try
                {
                    if (!integration.IsAvailable())
                    {
                        if (_logger is not null) LogNotAvailable(_logger, integration.Name);
                        continue;
                    }

                    var cleanup = integration.Setup(client);
                    _active.Add((integration, cleanup));
                    if (_logger is not null) LogSetupComplete(_logger, integration.Name);
                }
                catch (Exception ex)
                {
                    if (_logger is not null) LogSetupFailed(_logger, integration.Name, ex);
                    try { _onError?.Invoke(ex); } catch { /* swallow callback errors */ }
                }
            }
        }
    }

    /// <summary>Gets the names of all registered integrations.</summary>
    public IReadOnlyList<string> GetRegistered()
    {
        lock (_syncRoot)
        {
            return _registered.Select(i => i.Name).ToList();
        }
    }

    /// <summary>Gets the names of all active (successfully setup) integrations.</summary>
    public IReadOnlyList<string> GetActive()
    {
        lock (_syncRoot)
        {
            return _active.Select(a => a.Integration.Name).ToList();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_syncRoot)
        {
            foreach (var (integration, cleanup) in _active)
            {
                try
                {
                    cleanup.Dispose();
                }
                catch (Exception ex)
                {
                    if (_logger is not null) LogTeardownFailed(_logger, integration.Name, ex);
                }
            }

            _active.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Integration {Name} not available, skipping")]
    private static partial void LogNotAvailable(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Integration {Name} setup complete")]
    private static partial void LogSetupComplete(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integration {Name} setup failed, skipping")]
    private static partial void LogSetupFailed(ILogger logger, string name, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integration {Name} teardown failed")]
    private static partial void LogTeardownFailed(ILogger logger, string name, Exception ex);
}
