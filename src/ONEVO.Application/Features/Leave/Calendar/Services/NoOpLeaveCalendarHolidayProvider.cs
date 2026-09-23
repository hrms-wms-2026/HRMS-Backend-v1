namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed class NoOpLeaveCalendarHolidayProvider : ILeaveCalendarHolidayProvider
{
    public Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LeaveCalendarHoliday>>([]);
}
