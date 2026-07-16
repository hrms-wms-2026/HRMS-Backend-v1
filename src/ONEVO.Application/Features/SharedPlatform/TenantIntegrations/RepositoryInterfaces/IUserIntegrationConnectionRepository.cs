using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

public interface IUserIntegrationConnectionRepository
{
    Task<UserIntegrationConnection?> GetActiveAsync(
        Guid tenantId,
        Guid userId,
        string integrationKey,
        CancellationToken ct);

    Task AddAsync(UserIntegrationConnection connection, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
