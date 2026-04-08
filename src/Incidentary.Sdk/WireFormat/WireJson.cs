using System.Text.Json;

namespace Incidentary.Sdk.WireFormat;

/// <summary>
/// Provides the canonical <see cref="JsonSerializerOptions"/> for the Incidentary wire format.
/// </summary>
public static class WireJson
{
    /// <summary>
    /// Pre-configured options using the source-generated context.
    /// Snake_case naming, null-omitting, non-indented.
    /// </summary>
    public static JsonSerializerOptions Options => IncidentaryJsonContext.Default.Options;
}
