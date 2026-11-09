using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;

/// <summary>The tenant's max task rate row (wm_task_rate_settings, one per tenant).</summary>
public interface ITaskRateSettingRepository
{
    /// <summary>The tenant's row, or null when none has been saved (the code default applies).</summary>
    Task<TaskRateSetting?> GetForTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Creates or updates the tenant's row. The value is stored clamped to 0.5..1.0. Does not save: the
    /// caller commits. For the Settings edit flow (not wired to an endpoint yet).</summary>
    Task<TaskRateSetting> UpsertAsync(Guid tenantId, decimal maxTaskRate, Guid? updatedByEmployeeId, CancellationToken ct = default);
}
