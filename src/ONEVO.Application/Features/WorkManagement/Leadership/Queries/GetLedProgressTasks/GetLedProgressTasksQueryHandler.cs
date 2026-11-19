using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;

/// <summary>Same gate and scope as GetLedWorkProgressQueryHandler. Filters only ever narrow the led
/// scope; a project/module filter outside it, or a caller who leads nothing, gets an empty page.
/// An employeeId filter is applied on top of the scoped task set, so an employee with no assigned
/// task inside the scope also yields an empty page - never data from outside it.</summary>
public sealed class GetLedProgressTasksQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ILedProgressReadRepository reads)
    : IRequestHandler<GetLedProgressTasksQuery, Result<PagedResult<LedProgressTaskItem>>>
{
    public async Task<Result<PagedResult<LedProgressTaskItem>>> Handle(GetLedProgressTasksQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<PagedResult<LedProgressTaskItem>>.Forbidden("Authentication required.");

        var (pageNumber, pageSize) = LedProgressQueryOptions.ClampPaging(request.PageNumber, request.PageSize);
        var empty = new PagedResult<LedProgressTaskItem>([], pageNumber, pageSize, 0);

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<PagedResult<LedProgressTaskItem>>.Success(empty);

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        var objectiveIds = LedScopeFilter.Resolve(scope, request.ProjectId, request.ObjectiveId);
        if (objectiveIds.Count == 0)
            return Result<PagedResult<LedProgressTaskItem>>.Success(empty);

        LedTaskBucket? status = string.IsNullOrWhiteSpace(request.Status)
            ? null
            : LedProgressQueryOptions.Parse(request.Status, LedProgressQueryOptions.Statuses, LedTaskBucket.NotStarted);
        var filter = new LedProgressTaskFilter(
            objectiveIds,
            LedProgressQueryOptions.NormalizeSearch(request.Search),
            status,
            request.EmployeeId,
            LedProgressQueryOptions.Parse(request.Sort, LedProgressQueryOptions.TaskSorts, LedProgressTaskSort.EstimatedHours),
            LedProgressQueryOptions.IsDescending(request.Direction),
            LedProgressQueryOptions.Skip(pageNumber, pageSize),
            pageSize,
            LedProgressPeriod.Window(LedProgressPeriod.Parse(request.Period), LedProgressPeriod.UtcToday(clock.UtcNow)));
        var page = await reads.ListTasksAsync(tenantId, filter, ct);

        var items = page.Rows.Select(row => new LedProgressTaskItem(
            row.TaskId, row.ShortId, row.Title, LedProgressQueryOptions.WireStatus(row.Bucket), row.StatusName,
            row.EstimatedHours, row.DueDate, row.ProjectId, row.ProjectName, row.ObjectiveId, row.ObjectiveTitle,
            row.Assignees.Select(a => new LedTaskAssignee(a.EmployeeId, a.DisplayName)).ToList(),
            row.AssignmentCount > 1)).ToList();
        return Result<PagedResult<LedProgressTaskItem>>.Success(
            new PagedResult<LedProgressTaskItem>(items, pageNumber, pageSize, page.TotalCount));
    }
}
