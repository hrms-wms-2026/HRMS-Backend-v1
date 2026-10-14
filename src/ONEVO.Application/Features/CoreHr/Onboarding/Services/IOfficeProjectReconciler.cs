namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

public enum OfficeProjectReconcileOutcome
{
    Created,
    LeadSwitched,
    AlreadyAligned,
    NoOwnerEmployee,
    TenantInactive
}

/// <summary>Makes a tenant's Office project exist and be led by the tenant owner's employee.
/// Idempotent; safe to call on every startup and whenever the Office project is needed.</summary>
public interface IOfficeProjectReconciler
{
    Task<OfficeProjectReconcileOutcome> ReconcileAsync(Guid tenantId, CancellationToken ct = default);
}
