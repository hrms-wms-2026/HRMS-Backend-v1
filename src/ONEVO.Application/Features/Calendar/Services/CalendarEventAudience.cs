using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>Who besides the participants hears about an event change: its external guests (by email).</summary>
public static class CalendarEventAudience
{
    public static async Task<IReadOnlyList<string>> GuestEmailsAsync(ICalendarEventRepository events, Guid tenantId, Guid eventId, CancellationToken ct)
    {
        var guests = await events.GetGuestsForEventsAsync(tenantId, [eventId], ct);
        // A repository never returns null; a loose test double may.
        return guests is not null && guests.TryGetValue(eventId, out var list) ? list.Select(g => g.Email).ToList() : [];
    }
}
