using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfFeatureAccessGrantRepository : IFeatureAccessGrantRepository
{
    private readonly ApplicationDbContext _db;

    public EfFeatureAccessGrantRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FeatureAccessGrant>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var query = _db.FeatureAccessGrants
            .Where(g => g.TenantId == tenantId);

        var grants = await query.ToListAsync(ct);
        return grants;
    }
}
