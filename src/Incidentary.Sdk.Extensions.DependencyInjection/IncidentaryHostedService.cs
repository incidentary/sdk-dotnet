using Incidentary.Sdk;
using Microsoft.Extensions.Hosting;

namespace Incidentary.Sdk.Extensions.DependencyInjection;

internal sealed class IncidentaryHostedService : IHostedService
{
    private readonly IIncidentaryClient _client;

    public IncidentaryHostedService(IIncidentaryClient client)
    {
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
