using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IUserRepository
{
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<User?> GetActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<User?> GetByTenantAndEmailAsync(Guid tenantId, string normalizedEmail, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}
