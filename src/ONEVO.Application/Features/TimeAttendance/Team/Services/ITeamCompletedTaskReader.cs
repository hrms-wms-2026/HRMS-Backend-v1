namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>One completed task of one assignee: its estimate (null = unestimated) and when it was completed.</summary>
public sealed record TeamCompletedTask(Guid TaskId, Guid EmployeeId, decimal? EstimatedHours, DateTimeOffset CompletedAt);

/// <summary>
/// Completed tasks for the Work vs task hours widget. The task set mirrors Team Progress (wm_module_hour_summaries):
/// top-level tasks only (subtasks roll up into their parent), not soft-deleted, meeting tasks excluded, completed =
/// status MarksTaskComplete or ProgressPercent &gt;= 100, dated by CompletedAt.
/// </summary>
public interface ITeamCompletedTaskReader
{
    /// <summary>Tasks assigned to any of <paramref name="employeeIds"/> and completed in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<TeamCompletedTask>> ListCompletedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
