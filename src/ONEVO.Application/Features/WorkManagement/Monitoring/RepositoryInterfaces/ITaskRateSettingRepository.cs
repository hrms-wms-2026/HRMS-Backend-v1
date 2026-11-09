using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;

/// <summary>The max task rate per Legal Entity (wm_task_rate_settings, one row per tenant and Legal Entity).</summary>
public interface ITaskRateSettingRepository
{
    /// <summary>The Legal Entity's row, or null when none has been saved (the code default applies).</summary>
    Task<TaskRateSetting?> GetForLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    /// <summary>Creates or updates the Legal Entity's row. The value is stored clamped to 0.5..1.0. Does not save: the
    /// caller commits. For the Settings edit flow (not wired to an endpoint yet).</summary>
    Task<TaskRateSetting> UpsertAsync(Guid tenantId, Guid legalEntityId, decimal maxTaskRate, Guid? updatedByEmployeeId,
        CancellationToken ct = default);
}

public static class TaskRateSettingRepositoryExtensions
{
    /// <summary>The Legal Entity's limit, resolved: no Legal Entity or no row = <see cref="TaskRateLimit.Default"/>, a
    /// stored value clamped to 0.5..1.0.</summary>
    public static async Task<decimal> MaxTaskRateAsync(
        this ITaskRateSettingRepository repository, Guid tenantId, Guid? legalEntityId, CancellationToken ct = default)
        => legalEntityId is Guid id
            ? TaskRateLimit.Resolve((await repository.GetForLegalEntityAsync(tenantId, id, ct))?.MaxTaskRate)
            : TaskRateLimit.Default;
}
