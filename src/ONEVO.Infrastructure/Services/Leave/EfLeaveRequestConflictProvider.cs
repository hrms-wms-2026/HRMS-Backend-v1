using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Leave;

public sealed class EfLeaveRequestConflictProvider : ILeaveRequestConflictProvider
{
    private readonly ApplicationDbContext _db;

    public EfLeaveRequestConflictProvider(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<LeaveRequestCalendarConflict>> ListConflictsAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var rangeStart = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var rangeEndExclusive = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var rows = await (
            from participant in _db.CalendarEventParticipants.AsNoTracking()
            join calendarEvent in _db.PersonalCalendarEvents.AsNoTracking() on participant.EventId equals calendarEvent.Id
            where participant.EmployeeId == employeeId
                  && calendarEvent.TenantId == tenantId
                  && !calendarEvent.IsDeleted
                  && !calendarEvent.IsRecurrenceCancelled
                  && calendarEvent.EventStatus != CalendarEventStatuses.Cancelled
                  && participant.ResponseStatus != CalendarEventParticipantStatuses.Rejected
                  && calendarEvent.StartDate < rangeEndExclusive
                  && calendarEvent.EndDate > rangeStart
            select calendarEvent
        ).ToListAsync(ct);

        return rows
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .Select(row => new LeaveRequestCalendarConflict(
                "calendar",
                row.Title,
                row.StartDate,
                row.EndDate))
            .ToList();
    }
}
