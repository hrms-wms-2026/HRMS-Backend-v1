using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Calendar;

public class IcalNetRecurrenceExpander : ICalendarRecurrenceExpander
{
    private const int MaxOccurrences = 500;

    public IReadOnlyList<DateTimeOffset> Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to)
    {
        var pattern = new RecurrencePattern(recurrenceRule);
        var calendarEvent = new CalendarEvent
        {
            DtStart = new CalDateTime(seriesStart.UtcDateTime),
            RecurrenceRule = pattern
        };

        var rangeStart = new CalDateTime(from.UtcDateTime);
        var rangeEndUtc = to.UtcDateTime;

        return calendarEvent.GetOccurrences(rangeStart)
            .TakeWhile(o => o.Period.StartTime.AsUtc < rangeEndUtc)
            .Take(MaxOccurrences)
            .Select(o => new DateTimeOffset(o.Period.StartTime.AsUtc, TimeSpan.Zero))
            .ToList();
    }
}
