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
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
}
