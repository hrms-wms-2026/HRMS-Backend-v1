using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

/// <summary>
/// Builds the attendance day-detail activity block from snapshots when the nightly
/// summary row has not been written yet.
/// </summary>
public interface IActivityLiveDaySummary
{
    Task<ActivityDailySummaryDto?> ComposeAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct);
}
