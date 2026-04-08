namespace Incidentary.Sdk.Redaction;

/// <summary>Sanitizes event_attrs to comply with wire format constraints.</summary>
public static class EventAttrsSanitizer
{
    /// <summary>Maximum number of attribute keys allowed.</summary>
    public const int MaxKeys = 32;

    /// <summary>Maximum length for string attribute values.</summary>
    public const int MaxStringValueLength = 1024;

    /// <summary>
    /// Sanitizes a dictionary of event attributes. Returns a new dictionary.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    ///   <item>Null input returns null.</item>
    ///   <item>Maximum 32 keys; excess keys are dropped.</item>
    ///   <item>Allowed value types: string, int, long, float, double, bool, or null.</item>
    ///   <item>String values are truncated to 1,024 characters.</item>
    ///   <item>Nested objects and arrays are dropped (key omitted).</item>
    /// </list>
    /// </remarks>
    public static Dictionary<string, object>? Sanitize(Dictionary<string, object>? attrs)
    {
        if (attrs is null)
        {
            return null;
        }

        var result = new Dictionary<string, object>(Math.Min(attrs.Count, MaxKeys));
        var count = 0;

        foreach (var kvp in attrs)
        {
            if (count >= MaxKeys)
            {
                break;
            }

            if (!IsAllowedValue(kvp.Value, out var sanitized))
            {
                continue;
            }

            result[kvp.Key] = sanitized!;
            count++;
        }

        return result;
    }

    private static bool IsAllowedValue(object? value, out object? sanitized)
    {
        switch (value)
        {
            case null:
                sanitized = null!;
                return true;

            case string s:
                sanitized = s.Length > MaxStringValueLength ? s[..MaxStringValueLength] : s;
                return true;

            case int:
            case long:
            case float:
            case double:
            case bool:
                sanitized = value;
                return true;

            default:
                // Nested objects, arrays, and unsupported types are dropped.
                sanitized = null;
                return false;
        }
    }
}
