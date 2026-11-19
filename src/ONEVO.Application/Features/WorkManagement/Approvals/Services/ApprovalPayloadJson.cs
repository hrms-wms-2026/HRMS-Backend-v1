using System.Globalization;
using System.Text.Json;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Case-insensitive reads over a stored approval payload. Task payloads are PascalCase
/// (default serializer) while module and sprint payloads are camelCase, so every reader here
/// matches the property name ignoring case and can report the name exactly as stored.</summary>
internal static class ApprovalPayloadJson
{
    /// <summary>The payload's root object, or null when the JSON is empty, malformed or not an object.</summary>
    public static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool TryGet(JsonElement? root, string name, out JsonElement value, out string storedName)
    {
        if (root is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    storedName = property.Name;
                    return true;
                }
            }
        }
        value = default;
        storedName = name;
        return false;
    }

    public static string? GetString(JsonElement? root, string name)
        => TryGet(root, name, out var v, out _) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static decimal? GetDecimal(JsonElement? root, string name)
        => TryGet(root, name, out var v, out _) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    public static Guid? GetGuid(JsonElement? root, string name)
        => TryGet(root, name, out var v, out _) && v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var g) ? g : null;

    public static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
