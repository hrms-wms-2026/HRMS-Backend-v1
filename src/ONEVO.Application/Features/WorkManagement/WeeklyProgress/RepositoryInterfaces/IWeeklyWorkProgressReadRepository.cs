namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.RepositoryInterfaces;

/// <summary>One task's weight in a week: its estimate and its status category (done / active / not_started).</summary>
public sealed record WeekTaskWeight(Guid TaskId, decimal? EstimatedHours, string Category);

/// <summary>Weekly Work Progress reads that no existing repository covers (2026-10-28 design, 6.3).</summary>
public interface IWeeklyWorkProgressReadRepository
{
    /// <summary>Tasks assigned to the employee (meeting tasks excluded) that are due in [weekStart, weekEnd], or planned or
    /// clocked by the employee in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<WeekTaskWeight>> ListWeekTaskWeightsAsync(Guid tenantId, Guid employeeId, DateOnly weekStart,
        DateOnly weekEnd, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
