using System.Text.Json;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>Reads the meeting facts kept on an incident's evidence.</summary>
public static class CalendarIncidentEvidence
{
    public static string? MeetingTitle(string evidenceJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(evidenceJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String ? title.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
