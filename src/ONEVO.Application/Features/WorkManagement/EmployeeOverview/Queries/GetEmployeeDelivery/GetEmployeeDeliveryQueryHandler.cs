using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;

public sealed class GetEmployeeDeliveryQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeDeliveryQuery, Result<EmployeeDeliveryResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeDeliveryResponse>> Handle(GetEmployeeDeliveryQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeDeliveryResponse>.Forbidden("You do not have access to this employee's work.");

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var current = await MeasureAsync(tenantId, request.EmployeeId, period.Value!, ct);
        EmployeeDeliveryMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, period.Value!.Previous(), ct)
            : null;

        return Result<EmployeeDeliveryResponse>.Success(new EmployeeDeliveryResponse(
            period.Value!.From, period.Value.To,
            current.TasksAssigned, current.TasksCompleted, current.OnTimeCompleted, current.CompletedWithDueDate,
            current.StoryPointsAssigned, current.StoryPointsCompleted, previous));
    }

    private async Task<EmployeeDeliveryMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, EmployeePeriod period, CancellationToken ct)
    {
        var rows = await tasks.ListForEmployeePeriodAsync(tenantId, employeeId, period.From, period.To, ct);
        var asOf = period.To < clock.Today ? period.To : clock.Today;
        var s = EmployeeTaskPeriodCalculator.Compute(rows, asOf);
        return new EmployeeDeliveryMetrics(
            s.Assigned, s.Completed, s.OnTimeCompleted, s.CompletedWithDueDate, s.StoryPointsAssigned, s.StoryPointsCompleted);
    }
}
