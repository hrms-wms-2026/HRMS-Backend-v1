using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using CommonEmployeeRepository = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;

/// <summary>
/// Progress of an employee's onboarding/offboarding checklist tasks, grouped by task category
/// (falling back to the lifecycle name). Lifetime view: it does not follow the month period.
/// </summary>
public sealed class GetEmployeeChecklistOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeChecklistTaskRepository tasks,
    ICurrentUser currentUser,
    CommonEmployeeRepository? employees = null,
    ITaskAccessResolver? taskAccess = null,
    ITaskStatusChangeLogRepository? statusLogs = null,
    ITaskPercentageLogRepository? percentageLogs = null,
    ITaskStatusRepository? workStatuses = null)
    : IRequestHandler<GetEmployeeChecklistOverviewQuery, Result<EmployeeChecklistOverviewResponse>>
{
    public async Task<Result<EmployeeChecklistOverviewResponse>> Handle(
        GetEmployeeChecklistOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeChecklistOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (employees is null || taskAccess is null || statusLogs is null
            || percentageLogs is null || workStatuses is null)
        {
            var legacyRows = await tasks.ListByEmployeeAsync(tenantId, request.EmployeeId, ct);
            var legacyGroups = legacyRows
                .GroupBy(task => (Lifecycle: task.LifecycleType, Name: GroupName(task)))
                .Select(group => new EmployeeChecklistGroup(
                    group.Key.Name, group.Key.Lifecycle,
                    group.Count(task => task.Status == EmployeeChecklistTaskStatuses.Completed),
                    group.Count(task => task.Status == EmployeeChecklistTaskStatuses.Bypassed),
                    group.Count()))
                .OrderBy(group => group.LifecycleType == "onboarding" ? 0 : 1)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result<EmployeeChecklistOverviewResponse>.Success(
                new EmployeeChecklistOverviewResponse(legacyGroups));
        }

        var rows = await tasks.ListEffectiveByEmployeeAsync(tenantId, request.EmployeeId, ct);

        var assignees = await employees.GetByUserIdsAsync(
            tenantId, rows.Select(row => row.Task.AssignedToId).Distinct().ToList(), ct);
        var assigneeByUserId = assignees.GroupBy(employee => employee.UserId)
            .ToDictionary(group => group.Key, group => group.First());

        var completedByEmployeeId = new Dictionary<Guid, Guid>();
        var completedAtByChecklistTaskId = new Dictionary<Guid, DateTimeOffset>();
        foreach (var row in rows.Where(row => row.IsCompleted && row.Task.WorkTaskId is not null))
        {
            var taskId = row.Task.WorkTaskId!.Value;
            Guid? completionEmployeeId = null;
            DateTimeOffset? completionAt = null;
            var completionStatusLog = default(ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatusChangeLog);
            foreach (var log in (await statusLogs.GetForTaskAsync(tenantId, taskId, ct)).OrderByDescending(log => log.ChangedAt))
            {
                var status = await workStatuses.GetByIdForTenantAsync(tenantId, log.ToStatusId, ct);
                if (status?.MarksTaskComplete == true)
                {
                    completionStatusLog = log;
                    break;
                }
            }
            if (completionStatusLog is not null)
            {
                completionEmployeeId = completionStatusLog.EmployeeId;
                completionAt = completionStatusLog.ChangedAt;
            }

            var percentageLog = (await percentageLogs.GetForTaskAsync(tenantId, taskId, ct))
                .Where(log => log.NewPercent == 100)
                .OrderByDescending(log => log.ChangedAt)
                .FirstOrDefault();
            if (percentageLog is not null && (completionAt is null || percentageLog.ChangedAt > completionAt))
            {
                completionEmployeeId = percentageLog.EmployeeId;
                completionAt = percentageLog.ChangedAt;
            }
            if (completionEmployeeId is not null && completionAt is not null)
            {
                completedByEmployeeId[row.Task.Id] = completionEmployeeId.Value;
                completedAtByChecklistTaskId[row.Task.Id] = completionAt.Value;
            }
        }

        var actorIds = completedByEmployeeId.Values.Distinct().ToList();
        var actorEmployees = actorIds.Count == 0
            ? Array.Empty<ONEVO.Domain.Features.CoreHr.Entities.Employee>()
            : (await Task.WhenAll(actorIds.Select(id => employees.GetByIdAsync(tenantId, id, ct))))
                .Where(employee => employee is not null).Select(employee => employee!).ToArray();
        var actorNameById = actorEmployees.ToDictionary(
            employee => employee.Id, employee => $"{employee.FirstName} {employee.LastName}".Trim());

        var itemsByTaskId = new Dictionary<Guid, EmployeeChecklistTaskOverviewItem>();
        foreach (var row in rows)
        {
            assigneeByUserId.TryGetValue(row.Task.AssignedToId, out var assignee);
            var canOpen = false;
            if (row.Task.WorkTaskId is { } workTaskId)
            {
                var workAccess = await taskAccess.ResolveViewableTaskAsync(
                    tenantId, currentUser.UserId, workTaskId, ct);
                canOpen = workAccess.IsSuccess;
            }
            itemsByTaskId[row.Task.Id] = new EmployeeChecklistTaskOverviewItem(
                row.Task.Id,
                row.Task.TaskTitle,
                assignee?.Id ?? Guid.Empty,
                assignee is null ? "Unknown employee" : $"{assignee.FirstName} {assignee.LastName}".Trim(),
                row.Task.DueDate,
                row.WorkStatusName ?? row.Task.Status,
                row.CompletedAt ?? completedAtByChecklistTaskId.GetValueOrDefault(row.Task.Id),
                completedByEmployeeId.TryGetValue(row.Task.Id, out var actorId)
                    ? actorNameById.GetValueOrDefault(actorId)
                    : null,
                row.Task.WorkTaskId,
                row.WorkTaskShortId,
                row.WorkProjectId,
                canOpen);
        }

        var groups = rows
            .GroupBy(row => (Lifecycle: row.Task.LifecycleType, Name: GroupName(row.Task)))
            .Select(g => new EmployeeChecklistGroup(
                g.Key.Name,
                g.Key.Lifecycle,
                g.Count(row => row.IsCompleted),
                g.Count(row => row.Task.Status == EmployeeChecklistTaskStatuses.Bypassed),
                g.Count(),
                g.Select(row => itemsByTaskId[row.Task.Id]).ToList()))
            .OrderBy(g => g.LifecycleType == "onboarding" ? 0 : 1)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<EmployeeChecklistOverviewResponse>.Success(new EmployeeChecklistOverviewResponse(groups));
    }

    private static string GroupName(EmployeeChecklistTask task) =>
        !string.IsNullOrWhiteSpace(task.Category)
            ? task.Category.Trim()
            : task.LifecycleType.Length == 0
                ? "Checklist"
                : char.ToUpperInvariant(task.LifecycleType[0]) + task.LifecycleType[1..];
}
