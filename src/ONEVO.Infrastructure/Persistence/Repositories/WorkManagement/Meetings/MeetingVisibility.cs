using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

/// <summary>
/// The one EF expression every meeting list goes through - board, Work Tree, My Work, calendar (spec 5.1). It mirrors
/// <c>MeetingAccessResolver</c>: the series organizer; a series participant (soft-deleted ones are filtered out) without
/// a "remove" override for that occurrence; or an occurrence "add". No role ever widens it.
/// </summary>
public static class MeetingVisibility
{
    public static IQueryable<MeetingDetails> ForCaller(IQueryable<MeetingDetails> occurrences, ApplicationDbContext db, Guid callerEmployeeId)
        => occurrences.Where(d =>
            db.MeetingSeries.Any(s => s.Id == d.SeriesId && s.OrganizerEmployeeId == callerEmployeeId)
            || db.MeetingOccurrenceAttendees.Any(a => a.OccurrenceTaskId == d.TaskId
                && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == callerEmployeeId
                && a.Change == MeetingAttendeeChanges.Add)
            || (db.CalendarEventParticipants.Any(p => p.MeetingSeriesId == d.SeriesId && p.EmployeeId == callerEmployeeId)
                && !db.MeetingOccurrenceAttendees.Any(a => a.OccurrenceTaskId == d.TaskId
                    && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == callerEmployeeId
                    && a.Change == MeetingAttendeeChanges.Remove)));
}
