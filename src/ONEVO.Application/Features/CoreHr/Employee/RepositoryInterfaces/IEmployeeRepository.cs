using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

/// <summary>
/// RestrictToEmployeeIds, when non-null, is the authoritative visible-employee-id set already
/// resolved by IEmployeeAuthorityResolver.ResolveVisibilityAsync - ListVisibleAsync applies it
/// instead of the legacy EmployeeVisibilityScope coverage filter (scope is still honored when
/// RestrictToEmployeeIds is null, so GetVisibleByIdAsync and other scope-only callers are
/// unaffected). An empty (non-null) collection means "nothing visible".
/// </summary>
public sealed record EmployeeListFilter(
    string? Search, Guid? DepartmentId, Guid? LegalEntityId, IReadOnlyCollection<Guid>? RestrictToEmployeeIds = null);

/// <summary>
/// Enables the employee-list repository to calculate attendance warnings in one batch query. A
/// null option means attendance-sensitive data must not be projected.
/// </summary>
public sealed record EmployeeListAttendanceOptions(DateTimeOffset UtcNow);

public interface IEmployeeRepository
{
    Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        EmployeeListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default,
        EmployeeListAttendanceOptions? attendanceOptions = null);

    Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        Guid employeeId,
        CancellationToken ct = default);

    /// <summary>Employees this specific user invited (invitation_tokens.created_by_id) who
    /// haven't yet accepted (UsedAt is null) or been revoked - a brand-new invitee has no active
    /// PrimaryEmployment position assignment yet, so pure coverage-based visibility can't include
    /// them, but the person who invited them still needs to see/track/resend that invitation.
    /// Deliberately independent of EmployeeVisibilityScope/ListVisibleAsync - only
    /// ListEmployeesQueryHandler merges this in; it is not coverage and must never leak into
    /// coverage-strict consumers like the offboarding feature.</summary>
    Task<IReadOnlyList<EmployeeListItemResponse>> ListInvitedPendingByInviterAsync(
        Guid tenantId, Guid inviterUserId, CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetByIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>The Employee row a fresh session should default its ActiveEmployeeId to: the
    /// user's only Employee row if they have exactly one, or - if they have more than one -
    /// the one with the most recent active PrimaryEmployment PositionAssignment.EffectiveFrom.
    /// Returns null if the user has no Employee row at all.</summary>
    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetDefaultForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>The caller's Employee row in a given legal entity, used when the company
    /// switcher posts a legal-entity id rather than an employee id.</summary>
    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetByUserAndLegalEntityAsync(
        Guid tenantId, Guid userId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>Tracked fetch for mutation - GetByIdAsync above is AsNoTracking(). Used by
    /// self-service profile update handlers that need to change and save an Employee row.</summary>
    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetTrackedByIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Reads the current PostgreSQL xmin system-column value for optimistic-concurrency
    /// display to the client (returned as the profile's "version" token).</summary>
    Task<uint?> GetVersionTokenAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Sets the EF shadow "xmin" original value on a tracked Employee instance so
    /// SaveChangesAsync raises DbUpdateConcurrencyException when the row was modified since the
    /// caller last read it. No-ops silently on an unparsable version, matching
    /// IOnboardingDraftRepository.SetExpectedVersion's precedent.</summary>
    void SetExpectedVersion(ONEVO.Domain.Features.CoreHr.Entities.Employee employee, string expectedVersion);

    Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Legal-entity-scoped duplicate check, used by onboarding to allow the same person
    /// to be invited into a different legal entity under the same tenant. EmailExistsAsync above
    /// stays tenant-wide and is retained for any caller that genuinely needs that broader check.</summary>
    Task<bool> EmployeeExistsInLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, string email, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Tenant-scoped uniqueness check matching the <c>ix_employees_tenant_id_employee_number</c>
    /// unique index. Includes soft-deleted rows so a number held by an archived employee is still
    /// treated as taken (the unique index has no soft-delete filter).</summary>
    Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Next sequence for <c>{prefix}-NNNN</c> suggestions within the tenant, based on the
    /// maximum existing numeric suffix for that prefix (including soft-deleted employees). Does not
    /// reserve the value — callers must re-check uniqueness at save/finalize.</summary>
    Task<int> GetNextEmployeeNumberSequenceAsync(Guid tenantId, string prefix, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Active (EmploymentStatus code "active") employee ids in a legal entity, used by
    /// IEmployeeAuthorityResolver to expand department-scoped and company-wide management
    /// coverage into concrete employee ids. Pass departmentIds to restrict to those departments
    /// (an empty collection returns no rows); pass null to return every active employee in the
    /// legal entity (used only for TargetCompany coverage - deliberately the one case where
    /// loading the full legal-entity employee set is unavoidable).</summary>
    Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid>? departmentIds, CancellationToken ct = default);

    /// <summary>Final tenant/legal-entity/active-status chokepoint filter used by
    /// IEmployeeAuthorityResolver: given a raw candidate id set gathered from several expansion
    /// paths (position coverage, department coverage, company-wide), returns only the subset that
    /// is actually tenant-owned, in the requested legal entity, and currently active - so a stale
    /// or cross-boundary id from any single expansion path can never leak into a resolved scope.</summary>
    Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsByIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>Batch tenant-scoped employee lookup by id, unfiltered by employment status
    /// (mirrors GetByIdAsync's lack of an active filter, deliberately - callers that need only
    /// active rows should intersect with ListActiveEmployeeIdsByIdsAsync). Used by
    /// IEmployeeAuthorityResolver's batch approval-inbox scope resolution to preload subjects and
    /// reporting-line ancestor/holder candidates without one query per id. Ids not found are
    /// simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee>> ListByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
