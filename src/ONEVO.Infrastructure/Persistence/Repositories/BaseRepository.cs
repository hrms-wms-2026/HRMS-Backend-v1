using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public abstract class BaseRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext _db;
    protected readonly ITenantContext _tenantContext;

    protected BaseRepository(ApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    protected IQueryable<T> Query() =>
        _db.Set<T>().Where(e => e.TenantId == _tenantContext.TenantId && !e.IsDeleted);
}
