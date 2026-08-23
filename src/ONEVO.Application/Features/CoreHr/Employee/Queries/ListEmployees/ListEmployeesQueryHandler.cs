using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

public class ListEmployeesQueryHandler : IRequestHandler<ListEmployeesQuery, Result<EmployeeListPageResponse>>
{
    private const string RequiredPermission = "employees:read";
    private const string AttendanceReadPermission = "attendance:read";

    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeAuthorityResolver _authorityResolver;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTime;

    public ListEmployeesQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeAuthorityResolver authorityResolver,
        ICurrentUser currentUser,
        IDateTimeProvider dateTime)
    {
        _employeeRepository = employeeRepository;
        _authorityResolver = authorityResolver;
        _currentUser = currentUser;
        _dateTime = dateTime;
    }

    public async Task<Result<EmployeeListPageResponse>> Handle(ListEmployeesQuery request, CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // The resolver is inherently legal-entity-scoped (EmployeeAuthorityVisibilityRequest.
        // LegalEntityId is required, not nullable - see EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0).
        // When the caller doesn't name one (the People list's default "all companies" state),
        // fall back to the actor's own default Employee row's legal entity - the same one the
        // company switcher would default a session to (GetDefaultForUserAsync). Coverage records
        // can never span legal entities (AddManualCoverageRecordCommandHandler requires owner and
        // covered target to be looked up in the same request.LegalEntityId), so this is
        // behavior-preserving relative to the legacy tenant-wide coverage scope, not a narrowing.
        var legalEntityId = request.LegalEntityId
            ?? (await _employeeRepository.GetDefaultForUserAsync(_currentUser.TenantId, _currentUser.UserId, ct))?.LegalEntityId;

        if (legalEntityId is null)
        {
            // No Employee row anywhere for this actor: nothing to resolve visibility against,
            // same documented Phase 1 limitation the legacy EmployeeVisibilityScope had.
            return Result<EmployeeListPageResponse>.Success(
                new EmployeeListPageResponse(Array.Empty<EmployeeListItemResponse>(), 0, page, pageSize));
        }

        var visibility = await _authorityResolver.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                _currentUser.UserId, legalEntityId.Value, RequiredPermission, true, EmployeeAuthorityPurpose.EmployeeListRead),
            ct);

        var includeAttendanceWarnings = _currentUser.HasPermission(AttendanceReadPermission);
        IReadOnlyList<EmployeeListItemResponse> items;
        int totalCount;

        if (visibility.EmployeeIds.Count == 0)
        {
            items = Array.Empty<EmployeeListItemResponse>();
            totalCount = 0;
        }
        else
        {
            // Deliberately NOT EmployeeVisibilityScope.Unrestricted(): ListVisibleAsync's
            // RestrictToEmployeeIds branch is what actually applies here, so this scope value is
            // never read - but if a future refactor ever drops the RestrictToEmployeeIds branch
            // or passes a null id set by mistake, this must fail closed (nothing visible) rather
            // than fail open (Unrestricted() would fall through to "every tenant employee").
            var noFallbackScope = new EmployeeVisibilityScope(
                false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
            var filter = new EmployeeListFilter(
                request.Search,
                request.DepartmentId,
                legalEntityId,
                visibility.EmployeeIds.ToHashSet());

            (items, totalCount) = includeAttendanceWarnings
                ? await _employeeRepository.ListVisibleAsync(
                    _currentUser.TenantId,
                    noFallbackScope,
                    filter,
                    page,
                    pageSize,
                    ct,
                    new EmployeeListAttendanceOptions(_dateTime.UtcNow))
                : await _employeeRepository.ListVisibleAsync(
                    _currentUser.TenantId,
                    noFallbackScope,
                    filter,
                    page,
                    pageSize,
                    ct);
        }

        // A brand-new invitee has no active position assignment yet, so pure coverage visibility
        // never includes them - but the person who invited them still needs to see/track/resend
        // that invitation until it's accepted, regardless of whether the resolver granted them
        // any managed visibility. Restricted to the resolved legal entity so an invite in a
        // different company never leaks into this legal-entity-scoped page.
        var pendingInvited = await _employeeRepository.ListInvitedPendingByInviterAsync(
            _currentUser.TenantId, _currentUser.UserId, ct);
        var existingIds = items.Select(i => i.Id).ToHashSet();
        var toAdd = pendingInvited
            .Where(i => !existingIds.Contains(i.Id) && i.LegalEntityId == legalEntityId)
            .Select(i => i with { AttendanceSummary = null })
            .ToList();
        if (toAdd.Count > 0)
        {
            items = items.Concat(toAdd).ToList();
            totalCount += toAdd.Count;
        }

        if (!includeAttendanceWarnings)
        {
            // Defense in depth for alternate repository implementations and test doubles: an
            // employee-list caller without attendance:read must never receive sensitive state.
            items = items.Select(i => i with { AttendanceSummary = null }).ToList();
        }

        return Result<EmployeeListPageResponse>.Success(
            new EmployeeListPageResponse(items, totalCount, page, pageSize));
    }
}
