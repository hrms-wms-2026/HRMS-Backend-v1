namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public interface ICalendarRecurrenceExpander
{
    /// <summary>Occurrence start instants for a master's RRULE within [from, to]. Capped at 500
    /// occurrences - R1's month-grid queries are always bounded to ~6 weeks so this is never
    /// realistically hit; a longer-running series just returns its first 500 matches in range.</summary>
    IReadOnlyList<DateTimeOffset> Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to);
}
