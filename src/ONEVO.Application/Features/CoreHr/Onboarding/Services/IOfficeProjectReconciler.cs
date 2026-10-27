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

    /// <summary>Best-effort <see cref="ReconcileAsync"/> for callers whose own work has already
    /// committed (employee creation): logs and returns null instead of throwing, so a failed
    /// alignment never fails the caller. The next trigger or startup run retries it.
    /// Cancellation still propagates.</summary>
    Task<OfficeProjectReconcileOutcome?> TryReconcileAsync(Guid tenantId, CancellationToken ct = default);
}
