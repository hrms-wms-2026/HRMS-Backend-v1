using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;

/// <summary>Access follows the shipped Plan 3A pattern (GetEmployeeWorkOverviewQueryHandler): the
/// shared coverage guard alone, no extra module-permission-or-self gate.</summary>
public sealed class GetEmployeeNeedsAttentionQueryHandler(
    IEmployeeReadAccessGuard guard,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeNeedsAttentionQuery, Result<EmployeeNeedsAttentionResponse>>
{
    public async Task<Result<EmployeeNeedsAttentionResponse>> Handle(GetEmployeeNeedsAttentionQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeNeedsAttentionResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var asOf = clock.Today;
        var rows = await tasks.ListOpenDueByAsync(tenantId, request.EmployeeId, asOf.AddDays(WorkActivityTaskRules.DueSoonDays), ct);
        return Result<EmployeeNeedsAttentionResponse>.Success(WorkActivityTaskRules.ToAttention(rows, asOf));
    }
}
