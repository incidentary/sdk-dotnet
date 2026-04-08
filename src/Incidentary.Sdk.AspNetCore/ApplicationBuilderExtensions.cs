using Microsoft.AspNetCore.Builder;

namespace Incidentary.Sdk.AspNetCore;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the Incidentary middleware to the ASP.NET Core request pipeline.
    /// Should be added early in the pipeline (before routing/auth) for accurate timing.
    /// </summary>
    public static IApplicationBuilder UseIncidentary(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IncidentaryMiddleware>();
    }
}
