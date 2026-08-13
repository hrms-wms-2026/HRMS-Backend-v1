namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

/// <summary>
/// Rebuilds a single employee's <c>activity_daily_summary</c> row for a calendar date.
/// Idempotent: repeated calls replace totals rather than adding them.
/// </summary>
public interface IActivityDailySummaryRebuilder
{
    Task RebuildAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct);
}
