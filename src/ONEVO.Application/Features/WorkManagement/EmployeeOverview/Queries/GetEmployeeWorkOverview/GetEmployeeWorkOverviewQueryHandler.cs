using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;

public sealed class GetEmployeeWorkOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeWorkOverviewQuery, Result<EmployeeWorkOverviewResponse>>
{
    public async Task<Result<EmployeeWorkOverviewResponse>> Handle(GetEmployeeWorkOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeWorkOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var rows = await tasks.ListForEmployeePeriodAsync(tenantId, request.EmployeeId, period.Value!.From, period.Value.To, ct);
        var asOf = period.Value.To < clock.Today ? period.Value.To : clock.Today;
        var stats = EmployeeTaskPeriodCalculator.Compute(rows, asOf);

        int? onTimeRate = stats.CompletedWithDueDate == 0
            ? null
            : EmployeeTaskPeriodCalculator.Percent(stats.OnTimeCompleted, stats.CompletedWithDueDate);

        return Result<EmployeeWorkOverviewResponse>.Success(new EmployeeWorkOverviewResponse(
            period.Value.From, period.Value.To,
            stats.Assigned, stats.Completed, stats.InProgress, stats.Overdue, stats.NotStarted,
            EmployeeTaskPeriodCalculator.Percent(stats.Completed, stats.Assigned),
            onTimeRate));
    }
}
