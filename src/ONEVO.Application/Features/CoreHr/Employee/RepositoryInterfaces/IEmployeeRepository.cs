using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

public sealed record EmployeeListFilter(string? Search, Guid? DepartmentId, Guid? LegalEntityId);

public interface IEmployeeRepository
{
    Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        EmployeeListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        Guid employeeId,
        CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetByIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> GetByUserIdAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

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

    Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
