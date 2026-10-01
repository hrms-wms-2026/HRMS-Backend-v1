using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;

public sealed class GetEmployeeRecentTasksQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeRecentTasksQuery, Result<EmployeeRecentTasksResponse>>
{
    public async Task<Result<EmployeeRecentTasksResponse>> Handle(GetEmployeeRecentTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeRecentTasksResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var rows = await tasks.ListRecentlyChangedAssignedAsync(tenantId, request.EmployeeId, WorkActivityTaskRules.MaxItems, ct);
        return Result<EmployeeRecentTasksResponse>.Success(new EmployeeRecentTasksResponse(rows.Select(WorkActivityTaskRules.ToItem).ToList()));
    }
}
