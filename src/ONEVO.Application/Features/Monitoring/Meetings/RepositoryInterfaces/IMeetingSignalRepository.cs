using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

public interface IMeetingSignalRepository
{
    Task AddRangeAsync(IEnumerable<MeetingSignal> signals, CancellationToken ct);

    Task<IReadOnlyList<MeetingSignal>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);
}
