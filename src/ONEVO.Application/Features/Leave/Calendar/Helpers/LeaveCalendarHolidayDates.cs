using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public static class LeaveCalendarHolidayDates
{
    public static IReadOnlyList<DateOnly> DistinctDates(
        IReadOnlyList<LeaveCalendarHoliday> holidays) =>
        holidays.Select(h => h.Date).Distinct().OrderBy(d => d).ToList();
}
