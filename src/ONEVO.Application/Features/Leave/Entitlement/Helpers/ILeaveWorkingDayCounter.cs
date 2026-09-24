namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public interface ILeaveWorkingDayCounter
{
    int CountWorkingDays(DateOnly from, DateOnly to, IReadOnlyCollection<int> standardWorkingDays);
}
