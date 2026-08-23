namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed record LeaveCalendarHoliday(
    DateOnly Date,
    string Name,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string Source);

public interface ILeaveCalendarHolidayProvider
{
    Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}
