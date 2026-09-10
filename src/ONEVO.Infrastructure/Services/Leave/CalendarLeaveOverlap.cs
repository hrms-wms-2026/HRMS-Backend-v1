using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Leave;

public static class CalendarLeaveOverlap
{
    public static bool Overlaps(DateTimeOffset eventStart, DateTimeOffset eventEnd, DateOnly startDate, DateOnly endDate)
    {
        var rangeStart = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var rangeEndExclusive = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return eventStart < rangeEndExclusive && eventEnd > rangeStart;
    }

    public static IReadOnlyList<DateOnly> DatesInRange(DateTimeOffset eventStart, DateTimeOffset eventEnd, DateOnly startDate, DateOnly endDate)
    {
        var from = DateOnly.FromDateTime(eventStart.UtcDateTime);
        var to = DateOnly.FromDateTime(eventEnd.UtcDateTime);
        if (eventEnd.TimeOfDay == TimeSpan.Zero && eventEnd > eventStart)
            to = to.AddDays(-1);
        if (from < startDate) from = startDate;
        if (to > endDate) to = endDate;
        var dates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
            dates.Add(date);
        return dates;
    }

    public static bool IsActiveEvent(CalendarEvent calendarEvent) =>
        !calendarEvent.IsDeleted
        && !calendarEvent.IsRecurrenceCancelled
        && calendarEvent.EventStatus != CalendarEventStatuses.Cancelled;
}
