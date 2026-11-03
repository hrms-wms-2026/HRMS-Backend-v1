using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>One attendee of one occurrence, before names are resolved. <see cref="Source"/> is "series" or "occurrence".</summary>
public sealed record EffectiveAttendee(string Kind, Guid? EmployeeId, string? Email, string? Response, string? ResponseReason, string Source);

/// <summary>
/// An occurrence's attendees are the series attendees minus its "remove" overrides plus its "add" overrides
/// (spec 4.2). There is at most one override per person per occurrence (unique indexes on employee and lower(email)),
/// and writers replace it, so the last instruction wins by construction.
/// </summary>
public static class EffectiveAttendees
{
    public const string FromSeries = "series";
    public const string FromOccurrence = "occurrence";

    public static IReadOnlyList<EffectiveAttendee> Compute(
        IReadOnlyCollection<CalendarEventParticipant> seriesParticipants,
        IReadOnlyCollection<CalendarEventGuest> seriesGuests,
        IReadOnlyCollection<MeetingOccurrenceAttendee> overrides)
    {
        var employeeOverrides = overrides.Where(o => o.AttendeeKind == MeetingAttendeeKinds.Employee && o.EmployeeId is not null)
            .ToDictionary(o => o.EmployeeId!.Value);
        var guestOverrides = overrides.Where(o => o.AttendeeKind == MeetingAttendeeKinds.Guest && o.Email is not null)
            .ToDictionary(o => o.Email!.ToLowerInvariant());

        var result = new List<EffectiveAttendee>();
        var seriesEmployees = new HashSet<Guid>();
        foreach (var p in seriesParticipants.Where(p => !p.IsDeleted))
        {
            seriesEmployees.Add(p.EmployeeId);
            if (employeeOverrides.TryGetValue(p.EmployeeId, out var o) && o.Change == MeetingAttendeeChanges.Remove)
                continue;
            result.Add(new EffectiveAttendee(MeetingAttendeeKinds.Employee, p.EmployeeId, null, p.ResponseStatus, p.ResponseReason, FromSeries));
        }
        foreach (var o in employeeOverrides.Values.Where(o => o.Change == MeetingAttendeeChanges.Add && !seriesEmployees.Contains(o.EmployeeId!.Value)))
            result.Add(new EffectiveAttendee(MeetingAttendeeKinds.Employee, o.EmployeeId, null, o.ResponseStatus, o.ResponseReason, FromOccurrence));

        var seriesEmails = new HashSet<string>();
        foreach (var g in seriesGuests)
        {
            var key = g.Email.ToLowerInvariant();
            seriesEmails.Add(key);
            if (guestOverrides.TryGetValue(key, out var o) && o.Change == MeetingAttendeeChanges.Remove)
                continue;
            result.Add(new EffectiveAttendee(MeetingAttendeeKinds.Guest, null, g.Email, null, null, FromSeries));
        }
        foreach (var (key, o) in guestOverrides.Where(kv => kv.Value.Change == MeetingAttendeeChanges.Add && !seriesEmails.Contains(kv.Key)))
            result.Add(new EffectiveAttendee(MeetingAttendeeKinds.Guest, null, o.Email, null, null, FromOccurrence));

        return result;
    }
}
