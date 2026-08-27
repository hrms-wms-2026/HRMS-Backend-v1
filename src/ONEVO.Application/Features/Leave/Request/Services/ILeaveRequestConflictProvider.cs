namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed record LeaveRequestCalendarConflict(
    string Source,
    string Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

public interface ILeaveRequestConflictProvider
{
    Task<IReadOnlyList<LeaveRequestCalendarConflict>> ListConflictsAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}

public sealed class NoOpLeaveRequestConflictProvider : ILeaveRequestConflictProvider
{
    public Task<IReadOnlyList<LeaveRequestCalendarConflict>> ListConflictsAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LeaveRequestCalendarConflict>>([]);
}
