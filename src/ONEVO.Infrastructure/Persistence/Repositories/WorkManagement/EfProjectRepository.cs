using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Identifier == identifier, ct);
    }

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        await _db.Projects.AddAsync(project, ct);
    }
}
