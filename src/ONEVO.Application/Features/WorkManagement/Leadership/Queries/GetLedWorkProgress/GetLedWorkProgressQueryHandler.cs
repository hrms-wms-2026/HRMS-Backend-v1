using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWorkProgress;

public sealed class GetLedWorkProgressQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    IWorkTaskRepository tasks,
    IProjectRepository projects)
    : IRequestHandler<GetLedWorkProgressQuery, Result<LedWorkProgressResponse>>
{
    private static readonly LedWorkProgressResponse Empty = new(LedWorkTotals.Zero, [], [], 0);

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
        var rows = await tasks.ListTopLevelProgressRowsAsync(tenantId, allObjectiveIds, ct);
        var bucketsByObjective = rows
            .GroupBy(row => row.ObjectiveId)
            .ToDictionary(g => g.Key, g => g.Select(row =>
                TaskProgressClassifier.Classify(row.MarksTaskComplete, row.ProgressPercent, row.DueDate, today)).ToList());

        var overdueLimit = Math.Clamp(request.OverdueLimit, 1, 50);
        var overdueRows = await tasks.ListTopLevelOverdueAsync(tenantId, allObjectiveIds, today, overdueLimit, ct);
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
                var modules = group
                    .Select(head => new LedModuleProgress(head.ObjectiveId, head.Title, head.IsRootModule, head.EndDate,
                        Sum(head.SubtreeObjectiveIds.SelectMany(id => bucketsByObjective.GetValueOrDefault(id) ?? []))))
                    .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var project = projectRows[group.Key];
                return new LedProjectProgress(project.Id, project.Name, project.Identifier, Add(modules.Select(m => m.Totals)), modules);
            })
            .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totals = Add(projectProgress.Select(p => p.Totals));
        var overdue = overdueRows.Select(r => new LedOverdueTask(
            r.TaskId, r.ShortId, r.Title, r.ProjectId, r.ObjectiveId, r.DueDate, today.DayNumber - r.DueDate.DayNumber,
            r.AssigneeEmployeeIds.Select(id => new LedTaskAssignee(id,
                names.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Unknown employee")).ToList())).ToList();

        return Result<LedWorkProgressResponse>.Success(new LedWorkProgressResponse(totals, projectProgress, overdue, totals.Overdue));
    }

    private static LedWorkTotals Sum(IEnumerable<TaskProgressBucket> buckets)
    {
        int completed = 0, overdue = 0, inProgress = 0, notStarted = 0;
        foreach (var bucket in buckets)
        {
            switch (bucket)
            {
                case TaskProgressBucket.Completed: completed++; break;
                case TaskProgressBucket.Overdue: overdue++; break;
                case TaskProgressBucket.InProgress: inProgress++; break;
                default: notStarted++; break;
            }
        }
        return new LedWorkTotals(completed + overdue + inProgress + notStarted, completed, inProgress, notStarted, overdue);
    }

    private static LedWorkTotals Add(IEnumerable<LedWorkTotals> parts) => parts.Aggregate(LedWorkTotals.Zero, (a, b) =>
        new LedWorkTotals(a.Total + b.Total, a.Completed + b.Completed, a.InProgress + b.InProgress, a.NotStarted + b.NotStarted, a.Overdue + b.Overdue));
}
