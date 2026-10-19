using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// Read-side lookups for the Add Employee wizard's Reporting and Work setup steps, plus the
/// single write that carries the wizard's per-employee clock-in overrides onto the new employee.
/// </summary>
public interface IOnboardingWizardLookupService
{
    /// <summary>
    /// The chosen manager's reporting chain, organisation root first and the manager last.
    /// Empty when the manager is not an employee of the tenant.
    /// </summary>
    Task<IReadOnlyList<ReportingChainNodeResponse>> GetReportingChainAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default);

    /// <summary>
    /// What clock-in evidence a not-yet-created employee would inherit: photo from the work
    /// mode, location capture from the monitoring policy chain (work mode → position →
    /// department → legal entity/tenant). The role tier is skipped - the new hire has no role yet.
    /// </summary>
    Task<OnboardingClockInDefaultsResponse> GetClockInDefaultsAsync(
        Guid tenantId, Guid legalEntityId, Guid workModeId, Guid? positionId, Guid? departmentId,
        CancellationToken ct = default);

    /// <summary>
    /// Employees with an active primary seat in <paramref name="legalEntityId"/> whose name or
    /// position matches <paramref name="search"/> (case-insensitive), best matches first, capped.
    /// </summary>
    Task<IReadOnlyList<ManagerCandidateResponse>> SearchManagerCandidatesAsync(
        Guid tenantId, Guid legalEntityId, string search, int limit, CancellationToken ct = default);

    /// <summary>Tracks a new override row; persisted by the caller's next SaveChanges.</summary>
    Task AddEmployeeOverrideAsync(EmployeeMonitoringOverride entity, CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="fileId"/> may be attached to a draft as its profile photo: an
    /// existing, undeleted employee_avatar-compatible upload made by <paramref name="userId"/>.
    /// </summary>
    Task<bool> IsUsableDraftAvatarAsync(Guid tenantId, Guid userId, Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Links a draft's profile photo to the employee finalize just created. Best effort: the
    /// employee already exists, so a failure here leaves them without a photo rather than
    /// undoing the onboarding. Must run after the employee row is committed.
    /// </summary>
    Task LinkDraftAvatarAsync(Guid tenantId, Guid employeeId, Guid fileId, CancellationToken ct = default);
}
