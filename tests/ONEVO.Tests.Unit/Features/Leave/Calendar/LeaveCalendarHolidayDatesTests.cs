using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public sealed class LeaveCalendarHolidayDatesTests
{
    [Fact]
    public void DistinctDates_DedupesAndOrders()
    {
        var le = Guid.NewGuid();
        IReadOnlyList<LeaveCalendarHoliday> rows =
        [
            new(new DateOnly(2026, 1, 2), "B", le, null, "calendar"),
            new(new DateOnly(2026, 1, 1), "A", le, null, "calendar"),
            new(new DateOnly(2026, 1, 2), "B", le, null, "nager"),
        ];

        var dates = LeaveCalendarHolidayDates.DistinctDates(rows);

        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)], dates);
    }
}
