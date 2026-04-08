using Incidentary.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Incidentary.Sdk.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Incidentary SDK client as a singleton in the DI container.
    /// Includes a hosted service for graceful shutdown that calls <see cref="IAsyncDisposable.DisposeAsync"/>
    /// to flush pending events. Requires the Generic Host (<c>IHost</c>) lifecycle — in non-hosted
    /// console applications, call <c>DisposeAsync</c> on the client manually before exit to ensure
    /// the final flush completes.
    /// </summary>
    public static IServiceCollection AddIncidentary(
        this IServiceCollection services,
        Action<IncidentaryClientOptions> configure)
    {
        services.Configure(configure);

        services.TryAddSingleton<IIncidentaryClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IncidentaryClientOptions>>().Value;
            var logger = sp.GetService<ILogger<IncidentaryClient>>();
            return new IncidentaryClient(options, logger);
        });

        // Register hosted service for graceful shutdown
        services.AddHostedService<IncidentaryHostedService>();

        return services;
    }
}
