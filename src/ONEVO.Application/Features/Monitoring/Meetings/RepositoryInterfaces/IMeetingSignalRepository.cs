using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

public interface IMeetingSignalRepository
{
    Task AddRangeAsync(IEnumerable<MeetingSignal> signals, CancellationToken ct);

    /// <summary>
    /// captured_at values this device already stored among <paramref name="capturedAts"/> —
    /// lets ingest skip rows re-sent after a lost response.
    /// </summary>
    Task<IReadOnlySet<DateTimeOffset>> GetExistingCapturedAtsAsync(
        Guid tenantId,
        Guid agentDeviceId,
        IReadOnlyCollection<DateTimeOffset> capturedAts,
        CancellationToken ct);

    Task<IReadOnlyList<MeetingSignal>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<MeetingSignal>> GetAllByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<MeetingSignal>> GetByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
}
