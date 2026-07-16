using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;

namespace ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

public sealed class EfPositionRepository : BaseRepository<Position>, IPositionRepository
{
    public EfPositionRepository(ApplicationDbContext db, ITenantContext tenantContext)
        : base(db, tenantContext) { }

    public async Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Query().FirstOrDefaultAsync(p => p.Id == id, ct);
}
