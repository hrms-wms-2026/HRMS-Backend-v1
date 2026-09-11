using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfUserPermissionOverrideRepository : IUserPermissionOverrideRepository
{
    private readonly ApplicationDbContext _db;

    public EfUserPermissionOverrideRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserPermissionOverrideGrant>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default)
    {
        var query = _db.UserPermissionOverrides
            .Where(o => o.UserId == userId && o.TenantId == tenantId)
            .Join(
                _db.Permissions,
                o => o.PermissionId,
                p => p.Id,
                (o, p) => new UserPermissionOverrideGrant(p.Code, o.GrantType));

        var grants = await query.ToListAsync(ct);
        return grants;
    }

    public async Task AddAsync(UserPermissionOverride grant, CancellationToken ct = default)
    {
        await _db.UserPermissionOverrides.AddAsync(grant, ct);
    }
}
