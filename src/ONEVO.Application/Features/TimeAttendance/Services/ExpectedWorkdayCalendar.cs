namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record ExpectedWorkdays(
    DateOnly PeriodFrom, DateOnly PeriodTo,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    IReadOnlySet<DateOnly> ExpectedDates,
    IReadOnlySet<DateOnly> HolidayDates)
{
    public int Count => ExpectedDates.Count;
    public bool InEffectiveRange(DateOnly d) =>
        EffectiveFrom is DateOnly f && EffectiveTo is DateOnly t && d >= f && d <= t;
}

/// <summary>The days an employee was expected to work in a period: the legal entity's standard
/// working weekdays (General settings) minus synced holidays, clipped to hire date, termination date
/// and today. Attendance rows only exist once someone clocks in, so they can never be the source of
/// "expected" days - that was the 0/0 vs 1/1 bug.</summary>
public static class ExpectedWorkdayCalendar
{
    public static int IsoWeekday(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    public static ExpectedWorkdays Build(
        IReadOnlySet<int> workingWeekdays, IReadOnlySet<DateOnly> holidays,
        DateOnly from, DateOnly to, DateOnly hireDate, DateOnly? terminationDate, DateOnly today)
    {
        var start = from > hireDate ? from : hireDate;
        var end = to;
        if (terminationDate is DateOnly term && term < end) end = term;
        if (today < end) end = today;

        var inRangeHolidays = holidays.Where(h => h >= from && h <= to).ToHashSet();
        if (start > end)
            return new ExpectedWorkdays(from, to, null, null, new HashSet<DateOnly>(), inRangeHolidays);

        var expected = new HashSet<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(1))
            if (workingWeekdays.Contains(IsoWeekday(d)) && !inRangeHolidays.Contains(d))
                expected.Add(d);

        return new ExpectedWorkdays(from, to, start, end, expected, inRangeHolidays);
    }
}
