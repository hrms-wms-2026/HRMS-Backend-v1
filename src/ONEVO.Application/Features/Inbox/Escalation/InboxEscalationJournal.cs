using System.Text.Json;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Escalation;

/// <summary>Reads what <see cref="InboxEscalationRecorder"/> wrote into an escalation journal row.</summary>
public static class InboxEscalationJournal
{
    /// <summary>The employees the escalation went to (empty when it was only logged).</summary>
    public static IReadOnlyList<Guid> EscalatedTo(InboxDecisionJournalEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(entry.AfterJson ?? "{}");
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("escalatedTo", out var to) && to.ValueKind == JsonValueKind.Array
                ? to.EnumerateArray().Where(v => v.TryGetGuid(out _)).Select(v => v.GetGuid()).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
