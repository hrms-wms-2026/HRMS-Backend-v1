using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Leave;

public sealed class EfLeaveHolidayProvider : ILeaveHolidayProvider, ILeaveCalendarHolidayProvider
{
    private readonly ApplicationDbContext _db;

    public EfLeaveHolidayProvider(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId,
        Guid? legalEntityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var holidays = await ListHolidayEventsAsync(tenantId, startDate, endDate, ct);
        return holidays.Select(h => h.Date).Distinct().OrderBy(d => d).ToList();
    }

    async Task<IReadOnlyList<LeaveCalendarHoliday>> ILeaveCalendarHolidayProvider.ListHolidaysAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        return await ListHolidayEventsAsync(tenantId, startDate, endDate, ct);
    }

    private async Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidayEventsAsync(
        Guid tenantId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var rangeStart = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var rangeEndExclusive = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var events = await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(calendarEvent =>
                calendarEvent.TenantId == tenantId
                && calendarEvent.SourceType == CalendarEventSourceTypes.Holiday
                && !calendarEvent.IsDeleted
                && !calendarEvent.IsRecurrenceCancelled
                && calendarEvent.EventStatus != CalendarEventStatuses.Cancelled
                && calendarEvent.StartDate < rangeEndExclusive
                && calendarEvent.EndDate > rangeStart)
            .ToListAsync(ct);

        return events
            .SelectMany(calendarEvent => CalendarLeaveOverlap.DatesInRange(calendarEvent.StartDate, calendarEvent.EndDate, startDate, endDate)
                .Select(date => new LeaveCalendarHoliday(date, calendarEvent.Title, null, null, "calendar")))
            .GroupBy(holiday => (holiday.Date, holiday.Name))
            .Select(group => group.First())
            .OrderBy(holiday => holiday.Date)
            .ToList();
    }
}
