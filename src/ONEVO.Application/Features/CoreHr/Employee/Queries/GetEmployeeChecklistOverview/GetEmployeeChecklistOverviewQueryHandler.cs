using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;

/// <summary>
/// Progress of an employee's onboarding/offboarding checklist tasks, grouped by task category
/// (falling back to the lifecycle name). Lifetime view: it does not follow the month period.
/// </summary>
public sealed class GetEmployeeChecklistOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeChecklistTaskRepository tasks,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeChecklistOverviewQuery, Result<EmployeeChecklistOverviewResponse>>
{
    public async Task<Result<EmployeeChecklistOverviewResponse>> Handle(
        GetEmployeeChecklistOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeChecklistOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var rows = await tasks.ListByEmployeeAsync(tenantId, request.EmployeeId, ct);

        var groups = rows
            .GroupBy(t => (Lifecycle: t.LifecycleType, Name: GroupName(t)))
            .Select(g => new EmployeeChecklistGroup(
                g.Key.Name,
                g.Key.Lifecycle,
                g.Count(t => t.Status == EmployeeChecklistTaskStatuses.Completed),
                g.Count(t => t.Status == EmployeeChecklistTaskStatuses.Bypassed),
                g.Count()))
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
