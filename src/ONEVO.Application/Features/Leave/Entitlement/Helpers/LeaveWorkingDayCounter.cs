namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public class LeaveWorkingDayCounter : ILeaveWorkingDayCounter
{
    public int CountWorkingDays(DateOnly from, DateOnly to, IReadOnlyCollection<int> standardWorkingDays)
    {
        if (to < from)
            return 0;

        var configured = standardWorkingDays.ToHashSet();
        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            if (configured.Contains(isoDay))
                count++;
        }

        return count;
    }
}
