using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface IExternalCalendarConnectionRepository
{
    Task AddAsync(ExternalCalendarConnection connection, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetByTenantUserProviderAsync(Guid tenantId, Guid userId, string provider, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<ExternalCalendarConnection?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalCalendarConnection>> GetForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>All non-disabled connections visible under the currently-switched tenant context
    /// (RLS + the EF tenant query filter already scope this to one tenant at a time — the sync
    /// job calls this once per tenant inside its own per-tenant loop, not across all tenants).</summary>
    Task<IReadOnlyList<ExternalCalendarConnection>> GetActiveAsync(CancellationToken ct = default);

    void Update(ExternalCalendarConnection connection);
    void Remove(ExternalCalendarConnection connection);
}
