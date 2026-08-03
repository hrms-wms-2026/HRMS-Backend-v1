using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db) => _db = db;

    public async Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);
    }
}
