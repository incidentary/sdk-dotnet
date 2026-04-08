using Microsoft.Extensions.DependencyInjection;

namespace Incidentary.Sdk.Extensions.Http;

public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Adds Incidentary trace context propagation and HTTP_OUT event recording to an HttpClient.
    /// </summary>
    public static IHttpClientBuilder AddIncidentaryTracing(this IHttpClientBuilder builder)
    {
        builder.AddHttpMessageHandler(sp =>
        {
            var client = sp.GetRequiredService<IIncidentaryClient>();
            return new IncidentaryDelegatingHandler(client);
        });
        return builder;
    }
}
