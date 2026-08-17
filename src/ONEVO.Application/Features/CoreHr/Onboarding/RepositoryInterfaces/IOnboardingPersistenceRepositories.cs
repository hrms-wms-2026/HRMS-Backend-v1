using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

public interface IAccessGrantRequestRepository
{
    Task AddAsync(AccessGrantRequest request, CancellationToken ct = default);

    /// <summary>Keyed on the onboarding draft rather than the employee: onboarding finalization
    /// submits this request before the employee/user exist, so the draft is the only stable
    /// correlation key while a request is pending.</summary>
    Task<AccessGrantRequest?> GetPendingByDraftAsync(Guid tenantId, Guid onboardingDraftId, Guid targetPositionId, Guid positionAccessTemplateId, CancellationToken ct = default);

    /// <summary>Loads a tracked request by id, scoped to the tenant, for the approve/reject
    /// decision flow.</summary>
    Task<AccessGrantRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Whether any Pending request exists for this draft, regardless of position or
    /// template. Used by FinalizeOnboardingDraftCommandHandler to distinguish "a decision is
    /// still outstanding" (block re-finalize) from "the draft is only flagged
    /// WaitingForPositionApproval because it hasn't been re-saved since a rejection" (allow
    /// re-finalize to re-evaluate and submit a fresh request).</summary>
    Task<bool> AnyPendingByDraftAsync(Guid tenantId, Guid onboardingDraftId, CancellationToken ct = default);

    /// <summary>Tenant-scoped, paged list for the position-approval queue (Position Approver
    /// Inbox). Only requests correlated to an onboarding draft (<see cref="AccessGrantRequest.OnboardingDraftId"/>
    /// not null) and matching <paramref name="actionType"/> are returned - the draft join is
    /// inner for exactly this reason. <paramref name="approvalStatus"/> and
    /// <paramref name="actionType"/> are the literal stored values (e.g. "Pending",
    /// "onboarding_position_access"), already normalized by the caller.</summary>
    Task<(IReadOnlyList<OnboardingAccessGrantRequestListItemResponse> Items, int TotalCount)> ListOnboardingRequestsAsync(
        Guid tenantId, string approvalStatus, string actionType, Guid? legalEntityId, Guid? requestedRoleId,
        string? search, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Tenant-scoped list of every Pending access-grant request, including both
    /// onboarding and position-change action types. Names are resolved with the same LEFT-join
    /// style as <see cref="ListOnboardingRequestsAsync"/>: a missing employee/position/requester
    /// must not drop the row. <see cref="PendingAccessGrantRequestResponse.EmployeeName"/> is
    /// null when <see cref="AccessGrantRequest.EmployeeId"/> is null.</summary>
    Task<IReadOnlyList<PendingAccessGrantRequestResponse>> ListPendingAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public sealed record ChecklistTemplateListFilter(
    string? TemplateType, Guid LegalEntityId, Guid? DepartmentId, Guid? PositionId, bool IncludeInactive);

public static class ChecklistTemplateMatchLevels
{
    public const string Position = "position";
    public const string Department = "department";
    public const string Company = "company";
}

public sealed record ChecklistTemplateMatch(ChecklistTemplate Template, string MatchLevel);

public interface IChecklistTemplateRepository
{
    /// <summary>The single source of truth for "does this template still apply to this
    /// draft" - used both by the matching endpoint and by finalization so the two can never
    /// drift. templateType is fixed to "onboarding" - offboarding templates are never returned
    /// here.</summary>
    Task<ChecklistTemplate?> GetActiveOnboardingAsync(
        Guid tenantId, Guid templateId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default);

    Task<ChecklistTemplate?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Tracked fetch for update/archive handlers - callers mutate the returned entity
    /// directly and call SaveChangesAsync; there is deliberately no separate Update() method,
    /// so a detached blind overwrite is not possible.</summary>
    Task<ChecklistTemplate?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ChecklistTemplate> Items, int TotalCount)> ListAsync(
        Guid tenantId, ChecklistTemplateListFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Active onboarding templates matching legalEntityId (required) and optionally
    /// departmentId/positionId, ordered position match first, then department match, then
    /// company/default - never returns offboarding templates.</summary>
    Task<IReadOnlyList<ChecklistTemplateMatch>> ListOnboardingMatchesAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default);

    Task AddAsync(ChecklistTemplate template, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IEmployeeChecklistTaskRepository
{
    /// <summary>newHireUserId resolves any ownerType == "employee" task's deferred
    /// assignedToId; anchorDate is the draft's start date, used only when editedTasksJson is
    /// null (template.TasksJson tasks carry a relative dueOffsetDays, not a literal date).
    /// Never mutates template.TasksJson.</summary>
    Task<IReadOnlyList<EmployeeChecklistTask>> InstantiateAsync(
        ChecklistTemplate template, Guid employeeId, Guid newHireUserId, string? editedTasksJson, DateOnly anchorDate, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeChecklistTask>> ListByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
