using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Common;

public class EfEmployeeIdentityResolver : IEmployeeIdentityResolver
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeIdentityResolver(ApplicationDbContext db) => _db = db;

    public async Task<Guid?> ResolveEmployeeIdAsync(Guid userId, Guid tenantId, CancellationToken ct)
        => await _db.Employees
            .Where(e => e.UserId == userId && e.TenantId == tenantId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
}
