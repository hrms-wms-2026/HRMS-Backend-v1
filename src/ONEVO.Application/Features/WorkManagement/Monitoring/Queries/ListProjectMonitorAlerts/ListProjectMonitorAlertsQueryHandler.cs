using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Queries.ListProjectMonitorAlerts;

public sealed class ListProjectMonitorAlertsQueryHandler
    : IRequestHandler<ListProjectMonitorAlertsQuery, Result<IReadOnlyList<MonitorAlertResponse>>>
{
    private readonly IProjectMonitorCallerResolver _callers;
    private readonly IMonitorAlertRepository _alerts;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberRepository _members;
    private readonly IWorkTaskRepository _tasks;

    public ListProjectMonitorAlertsQueryHandler(
        IProjectMonitorCallerResolver callers, IMonitorAlertRepository alerts, IWorkHierarchyService hierarchy,
        IProjectMemberRepository members, IWorkTaskRepository tasks)
    {
        _callers = callers;
        _alerts = alerts;
        _hierarchy = hierarchy;
        _members = members;
        _tasks = tasks;
    }

    public async Task<Result<IReadOnlyList<MonitorAlertResponse>>> Handle(ListProjectMonitorAlertsQuery query, CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(query.ProjectId, ct);
        if (!caller.IsSuccess)
            return Result<IReadOnlyList<MonitorAlertResponse>>.Failure(caller.Error!, caller.StatusCode ?? 403);
        var (tenantId, employeeId, project) = caller.Value!;

        var alerts = await _alerts.ListOpenForProjectAsync(tenantId, query.ProjectId, ct);
        if (alerts.Count == 0)
            return Result<IReadOnlyList<MonitorAlertResponse>>.Success([]);

        var tree = await _hierarchy.LoadTreeAsync(tenantId, query.ProjectId, ct);
        var seesAll = project.LeadId == employeeId || tree.Root?.OwnerId == employeeId;

        var taskIds = alerts.Where(a => a.TargetType == MonitorTargetTypes.Task).Select(a => a.TargetId).Distinct().ToList();
        var moduleByTask = taskIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _tasks.GetObjectiveIdsByTaskIdsAsync(tenantId, taskIds, ct);

        var allModules = tree.Root is { } root ? tree.AtOrBelow([root.Id]) : new HashSet<Guid>();
        var achievedScope = tree.AtOrBelow(allModules.Where(id => tree.Get(id)?.IsAchieved == true));
        bool InAchievedModule(Domain.Features.WorkManagement.Monitoring.Entities.MonitorAlert a) => a.TargetType switch
        {
            MonitorTargetTypes.Module => achievedScope.Contains(a.TargetId),
            MonitorTargetTypes.Task => moduleByTask.TryGetValue(a.TargetId, out var moduleId) && achievedScope.Contains(moduleId),
            _ => false,
        };

        IEnumerable<Domain.Features.WorkManagement.Monitoring.Entities.MonitorAlert> visible = alerts;
        if (!seesAll)
        {
            // Membership (or ownership) on a parent Module cascades to everything below it.
            var direct = (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, query.ProjectId, employeeId, ct)).ToList();
            var owned = alerts.Where(a => a.TargetType == MonitorTargetTypes.Module && tree.Get(a.TargetId)?.OwnerId == employeeId)
                .Select(a => a.TargetId);
            var visibleModules = tree.AtOrBelow(direct.Concat(owned));

            visible = alerts.Where(a => a.TargetType switch
            {
                MonitorTargetTypes.Module => visibleModules.Contains(a.TargetId),
                MonitorTargetTypes.Task => moduleByTask.TryGetValue(a.TargetId, out var moduleId) && visibleModules.Contains(moduleId),
                _ => true,   // sprints are project-level; every project member sees them
            });
        }

        return Result<IReadOnlyList<MonitorAlertResponse>>.Success(visible
            .Select(a => new MonitorAlertResponse(a.Id, a.TargetType, a.TargetId, a.TargetTitle, a.RuleCode,
                a.SubjectEmployeeId, a.Message, a.FirstDetectedAt, InAchievedModule(a)))
            .ToList());
    }
}
