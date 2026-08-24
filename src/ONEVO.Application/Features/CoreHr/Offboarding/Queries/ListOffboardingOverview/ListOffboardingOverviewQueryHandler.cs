using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public class ListOffboardingOverviewQueryHandler(
    IEmployeeVisibilityScopeResolver scopeResolver,
    IEmployeeRepository employeeRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListOffboardingOverviewQuery, Result<IReadOnlyList<OffboardingOverviewItemResponse>>>
{
    private static readonly HashSet<string> OpenStatuses = ["initiated", "in_progress"];

    public async Task<Result<IReadOnlyList<OffboardingOverviewItemResponse>>> Handle(
        ListOffboardingOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        // Deliberately never EmployeeVisibilityScope.Unrestricted() - see design spec §11.
        var scope = await scopeResolver.ResolveAsync(tenantId, currentUser.UserId, ct);

        var (allItems, _) = await employeeRepository.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), request.Page, request.PageSize, ct);

        // ListVisibleAsync always includes the caller's own employee row (self-visibility is
        // correct for the general Employees list) - this screen is specifically "who can I
        // offboard", and nobody can offboard themselves, so the caller's own row must never
        // appear here regardless of what coverage rows they happen to also hold.
        var items = allItems.Where(i => i.Id != scope.OwnEmployeeId).ToList();

        var employeeIds = items.Select(i => i.Id).ToList();
        var statuses = await offboardingRecordRepository.GetLatestStatusesByEmployeeIdsAsync(tenantId, employeeIds, ct);

        var result = items.Select(i =>
        {
            statuses.TryGetValue(i.Id, out var status);
            return new OffboardingOverviewItemResponse(
                i.Id, i.FullName, i.DepartmentName, i.PositionName,
                status, CanStartOffboarding: status is null || !OpenStatuses.Contains(status));
        }).ToList();

        return Result<IReadOnlyList<OffboardingOverviewItemResponse>>.Success(result);
    }
}
