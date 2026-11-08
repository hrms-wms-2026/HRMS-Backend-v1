namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>One open task assigned to one employee. AssigneeCount = all its assignees (its remaining hours are split
/// evenly between them). Open = its status does not mark tasks complete, exactly as the project monitor reads it.</summary>
public sealed record ForecastOpenTask(
    Guid TaskId, Guid EmployeeId, Guid ProjectId, string ProjectName, Guid ModuleId, string ModuleTitle, DateOnly ModuleEndDate,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int AssigneeCount);

/// <summary>An active Module membership of one employee.</summary>
public sealed record ForecastMemberModule(
    Guid EmployeeId, Guid ModuleId, string ModuleTitle, Guid ProjectId, string ProjectName, DateOnly ModuleEndDate);

/// <summary>Reads for the employee overtime forecast. Only active, non-deleted Modules of active, non-system projects;
/// tasks not deleted and not meetings (subtasks included, like the monitor's EDF rule).</summary>
public interface IForecastTaskReader
{
    Task<IReadOnlyList<ForecastOpenTask>> ListOpenAssignedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<ForecastMemberModule>> ListMemberModulesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);
}
