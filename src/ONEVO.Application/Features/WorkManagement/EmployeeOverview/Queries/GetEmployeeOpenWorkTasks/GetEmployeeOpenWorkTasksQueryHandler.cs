using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeOpenWorkTasks;

/// <summary>
/// Every open task assigned to the employee, whatever its due date - the work still on their plate,
/// shown while they are offboarded. Same item shape, buckets and order as the Overview Work card list.
/// </summary>
public sealed class GetEmployeeOpenWorkTasksQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeOpenWorkTasksQuery, Result<IReadOnlyList<EmployeeWorkTaskItem>>>
{
    public async Task<Result<IReadOnlyList<EmployeeWorkTaskItem>>> Handle(GetEmployeeOpenWorkTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<EmployeeWorkTaskItem>>.Failure(access.Error!, access.StatusCode ?? 400);

        var rows = await tasks.ListOpenForEmployeeAsync(tenantId, request.EmployeeId, ct);
        return Result<IReadOnlyList<EmployeeWorkTaskItem>>.Success(EmployeeWorkTaskItems.Build(rows, clock.Today));
    }
}
