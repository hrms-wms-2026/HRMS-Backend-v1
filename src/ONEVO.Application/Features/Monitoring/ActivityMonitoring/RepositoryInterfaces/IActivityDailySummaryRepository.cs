using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

public interface IActivityDailySummaryRepository
{
    Task<ActivityDailySummary?> GetAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct);

    Task<IReadOnlyList<ActivityDailySummary>> GetRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    Task UpsertAsync(ActivityDailySummary summary, CancellationToken ct);
}
