using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> ListAsync(
        TenantStatus? statusFilter,
        string? searchTerm,
        int skip,
        int take,
        CancellationToken ct = default);
    Task<int> CountAsync(
        TenantStatus? statusFilter,
        string? searchTerm,
        CancellationToken ct = default);
    /// <summary>Keyset page of every tenant ordered by Id, starting after <paramref name="afterId"/>
    /// (null: from the start). Stable while tenants are created during a scan, unlike offset paging.</summary>
    Task<IReadOnlyList<Tenant>> ListPageByIdAsync(Guid? afterId, int take, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
}

public interface ITenantStatusHistoryRepository
{
    Task AddAsync(TenantStatusHistory history, CancellationToken ct = default);
}
