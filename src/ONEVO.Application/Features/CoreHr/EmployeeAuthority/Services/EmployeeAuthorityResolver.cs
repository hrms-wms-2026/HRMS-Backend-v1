using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;

/// <summary>
/// Generic employee authority resolver: visibility (who can an actor see/manage) and approval
/// routing (who is the eligible approver for a subject employee). Composes only Application-layer
/// repository interfaces - no EF Core dependency - so it can be unit tested with fakes and lives
/// safely in the Application layer per the existing architecture tests.
///
/// Tenant context is always taken from ICurrentUser.TenantId; callers never pass a TenantId, and
/// every downstream repository call is tenant-scoped, so a foreign-tenant ActorUserId/
/// SubjectEmployeeId simply resolves to "not found" / "no visibility" rather than leaking data.
/// </summary>
public sealed class EmployeeAuthorityResolver : IEmployeeAuthorityResolver
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IPositionAssignmentRepository _positionAssignmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IEmployeeHierarchyClosureRepository _closureRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPermissionRepository _permissionRepository;

    public EmployeeAuthorityResolver(
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IEmployeeRepository employeeRepository,
        IPositionAssignmentRepository positionAssignmentRepository,
        IPositionRepository positionRepository,
        IEmployeeHierarchyClosureRepository closureRepository,
        IDepartmentRepository departmentRepository,
        IPermissionRepository permissionRepository)
    {
        _currentUser = currentUser;
        _clock = clock;
        _employeeRepository = employeeRepository;
        _positionAssignmentRepository = positionAssignmentRepository;
        _positionRepository = positionRepository;
        _closureRepository = closureRepository;
        _departmentRepository = departmentRepository;
        _permissionRepository = permissionRepository;
    }

    public async Task<EmployeeAuthorityVisibilityScope> ResolveVisibilityAsync(
        EmployeeAuthorityVisibilityRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;

        var actorEmployee = await _employeeRepository.GetByUserAndLegalEntityAsync(
            tenantId, request.ActorUserId, request.LegalEntityId, cancellationToken);

        var candidateIds = new HashSet<Guid>();
        var includesSelf = false;

        if (request.IncludeSelf && actorEmployee is not null)
        {
            candidateIds.Add(actorEmployee.Id);
            includesSelf = true;
        }

        var hasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
            request.ActorUserId, request.RequiredPermission, now, cancellationToken);

        if (hasPermission && actorEmployee is not null)
        {
            var actorAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(
                tenantId, actorEmployee.Id, cancellationToken);

            if (actorAssignment is not null)
            {
                await AddManagedVisibilityAsync(
                    tenantId, request.LegalEntityId, actorAssignment.PositionId, candidateIds, cancellationToken);
            }
        }

        if (candidateIds.Count == 0)
        {
            return new EmployeeAuthorityVisibilityScope(
                request.ActorUserId, request.LegalEntityId, includesSelf, Array.Empty<Guid>());
        }

        var finalIds = await _employeeRepository.ListActiveEmployeeIdsByIdsAsync(
            tenantId, request.LegalEntityId, candidateIds, cancellationToken);

        // The self channel is not permission-gated, but must still survive the same
        // tenant/legal-entity/active-status chokepoint every other candidate does.
        includesSelf = includesSelf && actorEmployee is not null && finalIds.Contains(actorEmployee.Id);

        return new EmployeeAuthorityVisibilityScope(request.ActorUserId, request.LegalEntityId, includesSelf, finalIds);
    }

    private async Task AddManagedVisibilityAsync(
        Guid tenantId, Guid legalEntityId, Guid ownerPositionId, HashSet<Guid> candidateIds, CancellationToken ct)
    {
        // ListCoverageByOwnerPositionAsync returns every coverage record owned by this position
        // regardless of status (it backs the coverage-management UI, which needs to show
        // inactive rows too) - the resolver must filter to active itself.
        var coverageRows = (await _positionRepository.ListCoverageByOwnerPositionAsync(
                tenantId, legalEntityId, ownerPositionId, ct))
            .Where(c => c.Status == ManagementCoverageRecord.StatusActive)
            .ToList();

        var coveredPositionIds = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetPosition && c.CoveredPositionId is not null)
            .Select(c => c.CoveredPositionId!.Value)
            .ToList();

        var coveredDepartmentIds = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetDepartment && c.CoveredDepartmentId is not null)
            .Select(c => c.CoveredDepartmentId!.Value)
            .ToList();

        var hasCompanyWideCoverage = coverageRows.Any(c => c.CoveredTargetType == ManagementCoverageRecord.TargetCompany);

        // Position coverage: direct holder(s) of each covered position, plus their full
        // reporting-line subtree (transitive downward visibility).
        var positionHolderIds = new HashSet<Guid>();
        foreach (var positionId in coveredPositionIds)
        {
            var holders = await _positionAssignmentRepository.GetActiveHoldersAsync(tenantId, positionId, ct);
            foreach (var holder in holders)
                positionHolderIds.Add(holder.EmployeeId);
        }

        if (positionHolderIds.Count > 0)
        {
            candidateIds.UnionWith(positionHolderIds);
            var descendantIds = await _closureRepository.GetDescendantEmployeeIdsAsync(tenantId, positionHolderIds, ct);
            candidateIds.UnionWith(descendantIds);
        }

        // Department coverage: employees directly in each covered department or any of its
        // descendant departments.
        var expandedDepartmentIds = new HashSet<Guid>(coveredDepartmentIds);
        foreach (var departmentId in coveredDepartmentIds)
        {
            var descendantDeptIds = await _departmentRepository.GetDescendantDepartmentIdsAsync(
                tenantId, legalEntityId, departmentId, ct);
            expandedDepartmentIds.UnionWith(descendantDeptIds);
        }

        if (expandedDepartmentIds.Count > 0)
        {
            var departmentEmployeeIds = await _employeeRepository.ListActiveEmployeeIdsAsync(
                tenantId, legalEntityId, expandedDepartmentIds, ct);
            candidateIds.UnionWith(departmentEmployeeIds);
        }

        // Company-wide coverage: every active employee in the legal entity.
        if (hasCompanyWideCoverage)
        {
            var allEmployeeIds = await _employeeRepository.ListActiveEmployeeIdsAsync(
                tenantId, legalEntityId, null, ct);
            candidateIds.UnionWith(allEmployeeIds);
        }
    }

    public async Task<Result<EmployeeApprovalRoute>> ResolveApproverAsync(
        EmployeeApprovalRouteRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;

        var subject = await _employeeRepository.GetByIdAsync(tenantId, request.SubjectEmployeeId, cancellationToken);
        if (subject is null || subject.LegalEntityId != request.LegalEntityId)
        {
            return Result<EmployeeApprovalRoute>.NotFound(
                "Subject employee was not found in the requested legal entity.");
        }

        // The subject's upward reporting chain (nearest manager first) is the walk order for the
        // reporting-line fallback tier, which must stay upward-only. It is NOT used to gate
        // coverage-based candidates any more - manual management coverage is authoritative and can
        // legitimately name an owner outside the subject's reporting line (e.g. an HR business
        // partner covering a department they don't formally manage). See
        // "Manual coverage owner eligibility correction" in the Part 0 report.
        var ancestorChain = await _closureRepository.GetAncestorChainEmployeeIdsAsync(
            tenantId, subject.Id, cancellationToken);

        // Coverage-based candidates only need the narrower guard: never the subject themselves,
        // and never a subordinate of the subject (which would let a report approve their own
        // manager's request - a reverse-approval hole regardless of how coverage is configured).
        var descendantsOfSubject = await _closureRepository.GetDescendantEmployeeIdsAsync(
            tenantId, new[] { subject.Id }, cancellationToken);
        var descendantSet = descendantsOfSubject.ToHashSet();

        var subjectAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(
            tenantId, subject.Id, cancellationToken);

        if (subjectAssignment is not null)
        {
            var positionCoverage = await _positionRepository.ListActiveCoverageByCoveredTargetAsync(
                tenantId, request.LegalEntityId, ManagementCoverageRecord.TargetPosition,
                subjectAssignment.PositionId, null, null, cancellationToken);

            var route = await TryResolveFromCoverageAsync(
                tenantId, request, positionCoverage, subject.Id, descendantSet,
                EmployeeApprovalRouteSource.PositionCoverage, now, cancellationToken);
            if (route is not null)
                return Result<EmployeeApprovalRoute>.Success(route);
        }

        if (subject.DepartmentId is { } subjectDepartmentId)
        {
            var departmentCoverage = await _positionRepository.ListActiveCoverageByCoveredTargetAsync(
                tenantId, request.LegalEntityId, ManagementCoverageRecord.TargetDepartment,
                null, subjectDepartmentId, null, cancellationToken);

            var route = await TryResolveFromCoverageAsync(
                tenantId, request, departmentCoverage, subject.Id, descendantSet,
                EmployeeApprovalRouteSource.DepartmentCoverage, now, cancellationToken);
            if (route is not null)
                return Result<EmployeeApprovalRoute>.Success(route);
        }

        foreach (var ancestorEmployeeId in ancestorChain)
        {
            var ancestorEmployee = await _employeeRepository.GetByIdAsync(tenantId, ancestorEmployeeId, cancellationToken);
            if (ancestorEmployee is null || ancestorEmployee.LegalEntityId != request.LegalEntityId)
                continue;

            var hasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
                ancestorEmployee.UserId, request.RequiredPermission, now, cancellationToken);
            if (!hasPermission)
                continue;

            var ancestorAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(
                tenantId, ancestorEmployeeId, cancellationToken);
            if (ancestorAssignment is null)
                continue;

            return Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                ancestorEmployeeId, ancestorEmployee.UserId, ancestorAssignment.PositionId,
                request.RequiredPermission, request.Purpose, EmployeeApprovalRouteSource.ReportingLine, null));
        }

        return Result<EmployeeApprovalRoute>.UnprocessableEntity(
            "No eligible approver was found for this employee and action.");
    }

    /// <summary>Walks one ordered set of coverage records (already sorted by OwnerOrder ascending)
    /// looking for the first owner-level whose resolved holder is eligible, legal-entity matched,
    /// and holds the required permission - mirroring GetCoverageResolutionQueryHandler's holder-
    /// disambiguation exactly: a single holder resolves automatically, multiple holders require
    /// ResponsibleEmployeeId, and an unresolved level is skipped (not treated as a dead end) so the
    /// next backup order is still tried.
    ///
    /// Manual management coverage is authoritative: the resolved owner is NOT required to be a
    /// reporting-line ancestor of the subject (an HR business partner or other manually configured
    /// owner outside the org chart is a legitimate, intended case - see "Manual coverage owner
    /// eligibility correction" in the Part 0 report). The only hierarchy-derived guard kept here is
    /// that the owner must not be a subordinate of the subject, which would otherwise let a report
    /// approve their own manager's request.</summary>
    private async Task<EmployeeApprovalRoute?> TryResolveFromCoverageAsync(
        Guid tenantId,
        EmployeeApprovalRouteRequest request,
        IReadOnlyList<ManagementCoverageRecord> records,
        Guid subjectEmployeeId,
        HashSet<Guid> descendantsOfSubject,
        EmployeeApprovalRouteSource source,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var record in records)
        {
            var ownerPosition = await _positionRepository.GetByIdAsync(tenantId, record.OwnerPositionId, ct);
            if (ownerPosition is null || !ownerPosition.IsActive)
                continue;

            var holders = await _positionAssignmentRepository.GetActiveHoldersAsync(tenantId, record.OwnerPositionId, ct);

            var resolvedHolder = holders.Count switch
            {
                0 => null,
                1 => holders[0],
                _ => record.ResponsibleEmployeeId is { } chosenId
                    ? holders.FirstOrDefault(h => h.EmployeeId == chosenId)
                    : null,
            };

            if (resolvedHolder is null)
                continue;

            // Never the subject's own position/self.
            if (resolvedHolder.EmployeeId == subjectEmployeeId)
                continue;

            // Never a subordinate of the subject - manual coverage is authoritative outside the
            // reporting line, but never in the reverse-approval direction.
            if (descendantsOfSubject.Contains(resolvedHolder.EmployeeId))
                continue;

            // Same tenant + same legal entity + active employee, in one chokepoint call - the
            // resolved holder's PositionAssignment being active (checked above via
            // GetActiveHoldersAsync) does not by itself guarantee the employee's own employment
            // status is still active.
            var activeCandidateIds = await _employeeRepository.ListActiveEmployeeIdsByIdsAsync(
                tenantId, request.LegalEntityId, new[] { resolvedHolder.EmployeeId }, ct);
            if (activeCandidateIds.Count == 0)
                continue;

            var candidateEmployee = await _employeeRepository.GetByIdAsync(tenantId, resolvedHolder.EmployeeId, ct);
            if (candidateEmployee is null)
                continue;

            var hasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
                candidateEmployee.UserId, request.RequiredPermission, now, ct);
            if (!hasPermission)
                continue;

            return new EmployeeApprovalRoute(
                resolvedHolder.EmployeeId,
                candidateEmployee.UserId,
                record.OwnerPositionId,
                request.RequiredPermission,
                request.Purpose,
                source,
                record.OwnerOrder);
        }

        return null;
    }
}
