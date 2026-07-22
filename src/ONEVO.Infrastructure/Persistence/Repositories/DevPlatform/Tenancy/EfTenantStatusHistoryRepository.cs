using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;

public class EfTenantStatusHistoryRepository : ITenantStatusHistoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantStatusHistoryRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TenantStatusHistory history, CancellationToken ct = default)
    {
        await _db.TenantStatusHistories.AddAsync(history, ct);
    }
}
