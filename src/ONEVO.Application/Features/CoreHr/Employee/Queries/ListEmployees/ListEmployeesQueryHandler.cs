using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

public class ListEmployeesQueryHandler : IRequestHandler<ListEmployeesQuery, Result<EmployeeListPageResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly ICurrentUser _currentUser;

    public ListEmployeesQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        ICurrentUser currentUser)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeListPageResponse>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // org:manage stays unrestricted for Org Structure (Departments/Positions), but the
        // Employees directory is always coverage-scoped, org:manage included - per explicit
        // 2026-08-18 product decision. No permission bypasses ListVisibleAsync's coverage filter
        // here anymore; EmployeeVisibilityScope.Unrestricted() is simply never constructed in
        // this handler.
        var scope = await _visibilityScopeResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct);

        var (items, totalCount) = await _employeeRepository.ListVisibleAsync(
            _currentUser.TenantId,
            scope,
            new EmployeeListFilter(request.Search, request.DepartmentId, request.LegalEntityId),
            page,
            pageSize,
            ct);

        // A brand-new invitee has no active position assignment yet, so pure coverage visibility
        // never includes them - but the person who invited them still needs to see/track/resend
        // that invitation until it's accepted. Merge in anyone this caller invited who hasn't
        // accepted yet and isn't already in the coverage-scoped page. Deliberately not folded
        // into ListVisibleAsync/EmployeeVisibilityScope itself - the offboarding feature reuses
        // those for a strictly-coverage-only guarantee that must not gain this exception (see
        // IEmployeeRepository.ListInvitedPendingByInviterAsync).
        var pendingInvited = await _employeeRepository.ListInvitedPendingByInviterAsync(
            _currentUser.TenantId, _currentUser.UserId, ct);
        var existingIds = items.Select(i => i.Id).ToHashSet();
        var toAdd = pendingInvited.Where(i => !existingIds.Contains(i.Id)).ToList();
        if (toAdd.Count > 0)
        {
            items = items.Concat(toAdd).ToList();
            totalCount += toAdd.Count;
        }

        return Result<EmployeeListPageResponse>.Success(
            new EmployeeListPageResponse(items, totalCount, page, pageSize));
    }
}
