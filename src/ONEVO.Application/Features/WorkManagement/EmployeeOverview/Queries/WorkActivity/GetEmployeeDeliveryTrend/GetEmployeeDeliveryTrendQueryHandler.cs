using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;

public sealed class GetEmployeeDeliveryTrendQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeDeliveryTrendQuery, Result<EmployeeDeliveryTrendResponse>>
{
    public async Task<Result<EmployeeDeliveryTrendResponse>> Handle(GetEmployeeDeliveryTrendQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeDeliveryTrendResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var endDay = request.To ?? clock.Today;
        var (from, to) = WorkActivityTaskRules.TrendWindow(endDay, WorkActivityTaskRules.TrendMonths);
        var completed = await tasks.ListCompletedAtForEmployeeAsync(tenantId, request.EmployeeId, from, to, ct);

        return Result<EmployeeDeliveryTrendResponse>.Success(new EmployeeDeliveryTrendResponse(
            WorkActivityTaskRules.MonthlyCompleted(completed, endDay, WorkActivityTaskRules.TrendMonths)));
    }
}
