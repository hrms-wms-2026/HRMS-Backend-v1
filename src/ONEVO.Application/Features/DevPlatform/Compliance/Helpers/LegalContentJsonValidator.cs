using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Validates the content_json field submitted alongside content_html/content_text. Returns
/// an error message when invalid, or null when the value is an acceptable editor document.
/// </summary>
public static class LegalContentJsonValidator
{
    public static string? Validate(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return "content_json must not be empty.";
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            return "content_json must be valid JSON.";
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "content_json must be a JSON object.";
            }
        }

        return null;
    }
}
