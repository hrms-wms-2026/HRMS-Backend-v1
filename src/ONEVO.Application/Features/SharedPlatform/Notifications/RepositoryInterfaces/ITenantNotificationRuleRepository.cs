using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

/// <summary>Every call is scoped by an explicit tenant id in addition to the EF tenant filter and RLS.</summary>
public interface ITenantNotificationRuleRepository
{
    Task<IReadOnlyList<TenantNotificationRule>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantNotificationRule?> GetTrackedAsync(Guid tenantId, string eventKey, CancellationToken ct = default);
    Task<TenantNotificationRule?> GetAsync(Guid tenantId, string eventKey, CancellationToken ct = default);
    Task AddAsync(TenantNotificationRule rule, CancellationToken ct = default);
    void Remove(TenantNotificationRule rule);
}
