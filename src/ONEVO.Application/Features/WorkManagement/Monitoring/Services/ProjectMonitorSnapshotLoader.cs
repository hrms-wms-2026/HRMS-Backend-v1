using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

public interface IProjectMonitorSnapshotLoader
{
    /// <summary>Loads the active Modules, sprints, tasks, members, clocked hours and the working
    /// calendar of one project - a fixed handful of queries, independent of the tree size.</summary>
    Task<ProjectMonitorSnapshot> LoadAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
}

public sealed class ProjectMonitorSnapshotLoader : IProjectMonitorSnapshotLoader
{
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly ISprintRepository _sprints;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly IWorkCalendarResolver _calendars;

    public ProjectMonitorSnapshotLoader(
        IObjectiveRepository objectives, IProjectMemberRepository members, ISprintRepository sprints,
        IWorkTaskRepository tasks, ITaskStatusRepository statuses, ITaskAssignmentRepository assignments,
        ITaskClockingSessionRepository sessions, IWorkCalendarResolver calendars)
    {
        _objectives = objectives;
        _members = members;
        _sprints = sprints;
        _tasks = tasks;
        _statuses = statuses;
        _assignments = assignments;
        _sessions = sessions;
        _calendars = calendars;
    }

    public async Task<ProjectMonitorSnapshot> LoadAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        var calendar = await _calendars.ForProjectAsync(tenantId, projectId, ct);

        var objectives = (await _objectives.GetAllByProjectIdAsync(tenantId, projectId, ct))
            .Where(o => o.IsActive && !o.IsDeleted)
            .ToList();
        var memberIdsByModule = (await _members.ListActiveForProjectAsync(tenantId, projectId, ct))
            .GroupBy(m => m.ObjectiveId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(m => m.EmployeeId).ToHashSet());
        // Manpower = everyone at or below the Module: its allocation is split across its sub-Modules.
        var tree = new ProjectModuleTree(objectives);
        var modules = objectives.Select(o => new MonitorModule(o.Id, o.ParentObjectiveId, o.CreatorPositionObjectiveId, o.Title, o.StartDate, o.EndDate,
                o.AllocatedHours, o.CompletedHours, o.IsAchieved,
                ModuleManpower.Count(tree, o.Id, memberIdsByModule)))
            .ToList();

        var sprints = (await _sprints.GetByProjectAsync(tenantId, projectId, ct))
            .Where(s => !s.IsDeleted)
            .Select(s => new MonitorSprint(s.Id, s.CreatorPositionObjectiveId, s.Name, s.Status, s.EndDate))
            .ToList();

        var activeModuleIds = objectives.Select(o => o.Id).ToHashSet();
        var workTasks = (await _tasks.GetByProjectAsync(tenantId, projectId, ct))
            .Where(t => !t.IsDeleted && activeModuleIds.Contains(t.ObjectiveId))
            .ToList();
        var taskIds = workTasks.Select(t => t.Id).ToList();

        var completeStatusIds = (await _statuses.GetProjectTemplateAsync(tenantId, projectId, ct))
            .Where(s => s.MarksTaskComplete)
            .Select(s => s.Id)
            .ToHashSet();
        // Statuses outside the project template (legacy per-Module ones) are looked up once each.
        foreach (var statusId in workTasks.Select(t => t.StatusId).Distinct().Where(id => !completeStatusIds.Contains(id)).ToList())
            if (await _statuses.GetByIdForTenantAsync(tenantId, statusId, ct) is { MarksTaskComplete: true })
                completeStatusIds.Add(statusId);

        var assigneesByTask = taskIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _assignments.GetByTaskIdsAsync(taskIds, ct))
                .GroupBy(a => a.TaskId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.EmployeeId).Distinct().ToList());
        IReadOnlyDictionary<Guid, int> clockedMinutesByTask = taskIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _sessions.GetTotalClosedSessionMinutesForTasksAsync(tenantId, taskIds, ct);

        var tasks = workTasks.Select(t => new MonitorTask(
            t.Id, t.ObjectiveId, t.CreatorPositionObjectiveId, t.SprintId, t.Title, t.DueDate, t.EstimatedHours,
            t.CompletedHours, completeStatusIds.Contains(t.StatusId),
            clockedMinutesByTask.GetValueOrDefault(t.Id) / 60m,
            assigneesByTask.GetValueOrDefault(t.Id) ?? []))
            .ToList();

        return new ProjectMonitorSnapshot(projectId, calendar, modules, sprints, tasks);
    }
}
