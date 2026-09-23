namespace ONEVO.Application.Common.Helpers;

public static class WorkDayHoursCalculator
{
    public static decimal? TryCompute(TimeOnly? start, TimeOnly? end, int? breakMinutes)
    {
        if (start is null || end is null)
            return null;
        return Compute(start.Value, end.Value, breakMinutes ?? 0);
    }

    public static decimal Compute(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var shiftMinutes = ShiftLength(start, end).TotalMinutes;
        var net = (decimal)shiftMinutes - breakMinutes;
        return decimal.Round(net / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public static TimeSpan ShiftLength(TimeOnly start, TimeOnly end)
    {
        var startMins = start.Hour * 60 + start.Minute;
        var endMins = end.Hour * 60 + end.Minute;
        var delta = endMins - startMins;
        if (delta <= 0)
            delta += 24 * 60;
        return TimeSpan.FromMinutes(delta);
    }

    public static (DateTime Start, DateTime End) ShiftInterval(DateOnly startDate, TimeOnly workStart, TimeOnly workEnd)
    {
        var start = startDate.ToDateTime(workStart);
        var end = startDate.ToDateTime(workEnd);
        if (workEnd <= workStart)
            end = startDate.AddDays(1).ToDateTime(workEnd);
        return (start, end);
    }
}
