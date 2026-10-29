using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;

/// <summary>Same gate and scope as GetLedWorkProgressQueryHandler. Filters only ever narrow the led
/// scope; a filter outside it, or a caller who leads nothing, gets an empty page. A week/month
/// period narrows both the employee rows and the unassigned totals to tasks due in that window.</summary>
public sealed class GetLedEmployeeHoursQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEmployeeRepository employees,
    IWorkLeadershipService leadership,
    ILedProgressReadRepository reads)
    : IRequestHandler<GetLedEmployeeHoursQuery, Result<LedEmployeeHoursPage>>
{
    public async Task<Result<LedEmployeeHoursPage>> Handle(GetLedEmployeeHoursQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId == Guid.Empty)
            return Result<LedEmployeeHoursPage>.Forbidden("Authentication required.");

        var (pageNumber, pageSize) = LedProgressQueryOptions.ClampPaging(request.PageNumber, request.PageSize);
        var empty = new LedEmployeeHoursPage([], pageNumber, pageSize, 0, LedUnassignedHours.Zero);

        var tenantId = currentUser.TenantId;
        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (caller?.LegalEntityId is not Guid legalEntityId)
            return Result<LedEmployeeHoursPage>.Success(empty);

        var scope = await leadership.ResolveLedScopeAsync(tenantId, caller.Id, legalEntityId, ct);
        var objectiveIds = LedScopeFilter.Resolve(scope, request.ProjectId, request.ObjectiveId);
        if (objectiveIds.Count == 0)
            return Result<LedEmployeeHoursPage>.Success(empty);

        var dueWindow = LedProgressPeriod.Window(
            LedProgressPeriod.Parse(request.Period), LedProgressPeriod.UtcToday(clock.UtcNow));
        var filter = new LedEmployeeHoursFilter(
            objectiveIds,
            LedProgressQueryOptions.NormalizeSearch(request.Search),
            LedProgressQueryOptions.Parse(request.Sort, LedProgressQueryOptions.EmployeeSorts, LedEmployeeHoursSort.TotalHours),
            LedProgressQueryOptions.IsDescending(request.Direction),
            LedProgressQueryOptions.Skip(pageNumber, pageSize),
            pageSize,
            dueWindow);
        var page = await reads.ListEmployeeHoursAsync(tenantId, filter, ct);
        var unassigned = await reads.GetUnassignedHoursAsync(tenantId, objectiveIds, dueWindow, ct);

        var items = page.Rows.Select(row => new LedEmployeeHoursItem(
            row.EmployeeId, row.DisplayName, row.CompletedHours, row.InProgressHours, row.NotStartedHours, row.TotalHours,
            row.TaskCount, row.SharedTaskCount, row.SharedHours)).ToList();
        return Result<LedEmployeeHoursPage>.Success(new LedEmployeeHoursPage(
            items, pageNumber, pageSize, page.TotalCount,
            new LedUnassignedHours(unassigned.CompletedHours, unassigned.InProgressHours, unassigned.NotStartedHours,
                unassigned.TotalHours, unassigned.TaskCount)));
    }
}
