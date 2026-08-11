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

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IChecklistTemplateRepository
{
    Task<ChecklistTemplate?> GetActiveOnboardingAsync(Guid tenantId, Guid templateId, Guid? departmentId, CancellationToken ct = default);
    Task AddAsync(ChecklistTemplate template, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IEmployeeChecklistTaskRepository
{
    Task<IReadOnlyList<EmployeeChecklistTask>> InstantiateAsync(ChecklistTemplate template, Guid employeeId, string? editedTasksJson, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeChecklistTask>> ListByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
