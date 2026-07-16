using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface IUserExternalIdentityRepository
{
    Task<UserExternalIdentity?> GetByTenantProviderAndSubjectAsync(
        Guid tenantId,
        string provider,
        string providerSubject,
        CancellationToken ct = default);

    Task AddAsync(UserExternalIdentity identity, CancellationToken ct = default);
}
