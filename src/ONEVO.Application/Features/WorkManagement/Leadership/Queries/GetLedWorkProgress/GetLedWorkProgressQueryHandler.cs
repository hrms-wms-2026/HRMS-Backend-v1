using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

public sealed class GetLedWorkProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    IWorkTaskRepository tasks,
    IModuleHourSummaryRepository hourSummaries,
    IProjectRepository projects)
    : IRequestHandler<GetLedWorkProgressQuery, Result<LedWorkProgressResponse>>
{
    private static readonly LedWorkProgressResponse Empty = new(LedHourTotals.Zero, [], [], 0, 0);

    public async Task<Result<LedWorkProgressResponse>> Handle(GetLedWorkProgressQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedWorkProgressResponse>.Forbidden("Authentication required.");

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedWorkProgressResponse>.Success(Empty);

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        if (scope.HeadModules.Count == 0)
            return Result<LedWorkProgressResponse>.Success(Empty);

        // UTC date - identical to GetMyTaskProgressQueryHandler, so My Day and My Team agree
        // (TaskProgressClassifier's own doc comment records this as a deliberate V1 assumption,
        // not something this handler introduces).
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var allObjectiveIds = scope.AllObjectiveIds.ToList();
        // One indexed read of the trigger-maintained per-Module totals - no task aggregation here.
        var summaries = await hourSummaries.ListByObjectiveIdsAsync(tenantId, allObjectiveIds, ct);

        var overdueLimit = Math.Clamp(request.OverdueLimit, 1, 50);
        var overdueRows = await tasks.ListTopLevelOverdueAsync(tenantId, allObjectiveIds, today, overdueLimit, ct);
        var overdueTotal = overdueRows.Count < overdueLimit
            ? overdueRows.Count
            : await tasks.CountTopLevelOverdueAsync(tenantId, allObjectiveIds, today, ct);
        var projectRows = (await projects.ListByIdsAsync(tenantId, scope.HeadModules.Select(h => h.ProjectId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id);
        var assigneeIds = overdueRows.SelectMany(r => r.AssigneeEmployeeIds).Distinct().ToList();
        var names = assigneeIds.Count == 0
            ? new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : await employees.ListByIdsAsync(tenantId, assigneeIds, ct);

        var projectProgress = scope.HeadModules
            .GroupBy(head => head.ProjectId)
            .Where(group => projectRows.ContainsKey(group.Key))
            .Select(group =>
            {
                var heads = group
                    .OrderBy(head => head.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(head => LedHourRollup.ForHead(head, summaries, today))
                    .ToList();
                var project = projectRows[group.Key];
                return new LedProjectProgress(project.Id, project.Name, project.Identifier,
                    LedHourRollup.Add(heads.Select(modules => modules[0].Hours)), heads.SelectMany(modules => modules).ToList());
            })
            .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hours = LedHourRollup.Add(projectProgress.Select(p => p.Hours));
        var modulesAtRisk = projectProgress.Sum(p => p.Modules.Count(m => m.Depth == 0 && m.AtRisk));
        var overdue = overdueRows.Select(r => new LedOverdueTask(
            r.TaskId, r.ShortId, r.Title, r.ProjectId, r.ObjectiveId, r.DueDate, today.DayNumber - r.DueDate.DayNumber,
            r.AssigneeEmployeeIds.Select(id => new LedTaskAssignee(id,
                names.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Unknown employee")).ToList())).ToList();

        return Result<LedWorkProgressResponse>.Success(new LedWorkProgressResponse(hours, projectProgress, overdue, overdueTotal, modulesAtRisk));
    }
}
