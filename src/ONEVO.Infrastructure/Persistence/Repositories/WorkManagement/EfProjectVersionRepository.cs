using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectVersionRepository : IProjectVersionRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectVersionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectVersion version, CancellationToken ct = default)
    {
        await _db.ProjectVersions.AddAsync(version, ct);
    }
}
