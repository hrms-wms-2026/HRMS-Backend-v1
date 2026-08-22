namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveHolidayProvider
{
    Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId,
        Guid? legalEntityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}

public sealed class NoOpLeaveHolidayProvider : ILeaveHolidayProvider
{
    public Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId,
        Guid? legalEntityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DateOnly>>([]);
}
