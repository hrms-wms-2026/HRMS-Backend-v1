using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfUserExternalIdentityRepository : IUserExternalIdentityRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserExternalIdentityRepository(ApplicationDbContext db) => _db = db;

    public Task<UserExternalIdentity?> GetByTenantProviderAndSubjectAsync(
        Guid tenantId,
        string provider,
        string providerSubject,
        CancellationToken ct = default) =>
        _db.UserExternalIdentities.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.Provider == provider && x.ProviderSubject == providerSubject,
            ct);

    public Task AddAsync(UserExternalIdentity identity, CancellationToken ct = default) =>
        _db.UserExternalIdentities.AddAsync(identity, ct).AsTask();
}

public sealed class EfTenantAuthPolicyRepository : ITenantAuthPolicyRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantAuthPolicyRepository(ApplicationDbContext db) => _db = db;

    public Task<TenantAuthPolicy?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.TenantAuthPolicies.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public Task AddAsync(TenantAuthPolicy policy, CancellationToken ct = default) =>
        _db.TenantAuthPolicies.AddAsync(policy, ct).AsTask();
}
