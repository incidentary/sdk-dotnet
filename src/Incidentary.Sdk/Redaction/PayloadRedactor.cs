using System.Text;
using System.Text.Json;

namespace Incidentary.Sdk.Redaction;

/// <summary>Redacts sensitive fields from JSON payloads and URL query parameters.</summary>
public static class PayloadRedactor
{
    /// <summary>Replacement value for sensitive fields.</summary>
    public const string RedactedValue = "<redacted>";

    /// <summary>Default sensitive field names (case-insensitive match).</summary>
    public static IReadOnlyList<string> DefaultSensitiveFields { get; } = new[]
    {
        "password", "passwd", "secret", "token", "api_key", "apikey",
        "authorization", "auth", "credential", "credentials",
        "credit_card", "creditcard", "card_number", "cardnumber",
        "cvv", "cvc", "ssn", "social_security",
        "email", "phone", "phone_number",
    };

    /// <summary>
    /// Redacts values of matching keys in a JSON string. Returns the redacted JSON.
    /// If the input is not valid JSON, returns the input unchanged (fail-open).
    /// </summary>
    public static string RedactJson(string json, IReadOnlyList<string>? extraFields = null)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        var fields = BuildFieldSet(extraFields);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        using (doc)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteRedacted(writer, doc.RootElement, fields);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>
    /// Redacts matching query parameters in a URL query string.
    /// Input: "foo=bar&amp;password=secret" Output: "foo=bar&amp;password=&lt;redacted&gt;"
    /// </summary>
    public static string RedactUrlParams(string queryString, IReadOnlyList<string>? extraFields = null)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return queryString;
        }

        var fields = BuildFieldSet(extraFields);
        var pairs = queryString.Split('&');
        var result = new string[pairs.Length];

        for (var i = 0; i < pairs.Length; i++)
        {
            var eqIndex = pairs[i].IndexOf('=');
            if (eqIndex < 0)
            {
                result[i] = pairs[i];
                continue;
            }

            var key = pairs[i][..eqIndex];
            var value = pairs[i][(eqIndex + 1)..];
            result[i] = IsSensitive(key, fields)
                ? $"{key}={RedactedValue}"
                : $"{key}={value}";
        }

        return string.Join('&', result);
    }

    /// <summary>
    /// Truncates a string to <paramref name="maxBytes"/> (UTF-8 byte count).
    /// Respects UTF-8 character boundaries so the result is always valid UTF-8.
    /// </summary>
    public static string Truncate(string input, int maxBytes = 4096)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (Encoding.UTF8.GetByteCount(input) <= maxBytes)
        {
            return input;
        }

        // Binary-search on char count to find the longest prefix that fits.
        // Each char is 1-4 UTF-8 bytes, so the char count is between maxBytes/4 and maxBytes.
        var lo = 0;
        var hi = input.Length;

        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (Encoding.UTF8.GetByteCount(input.AsSpan(0, mid)) <= maxBytes)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // lo is the largest char index where byte count <= maxBytes.
        // Ensure we don't split a surrogate pair.
        if (lo > 0 && char.IsHighSurrogate(input[lo - 1]))
        {
            lo--;
        }

        return input[..lo];
    }

    private static HashSet<string> BuildFieldSet(IReadOnlyList<string>? extraFields)
    {
        var set = new HashSet<string>(DefaultSensitiveFields, StringComparer.OrdinalIgnoreCase);
        if (extraFields is not null)
        {
            foreach (var field in extraFields)
            {
                set.Add(field);
            }
        }

        return set;
    }

    private static bool IsSensitive(string propertyName, HashSet<string> fields)
    {
        // Contains-match: if the property name contains any sensitive field name (case-insensitive).
        foreach (var field in fields)
        {
            if (propertyName.Contains(field, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element, HashSet<string> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitive(property.Name, fields))
                    {
                        writer.WriteStringValue(RedactedValue);
                    }
                    else
                    {
                        WriteRedacted(writer, property.Value, fields);
                    }
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedacted(writer, item, fields);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
