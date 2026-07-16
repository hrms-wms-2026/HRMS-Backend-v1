using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.Tenancy;

internal static class RoleTemplateJson
{
    public static string SerializeStringList(IReadOnlyList<string> items) =>
        JsonSerializer.Serialize(
            items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public static IReadOnlyList<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .Select(s => s.Trim())
                       .Distinct(StringComparer.Ordinal)
                       .ToList()
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
