using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveRepository : IObjectiveRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Objective objective, CancellationToken ct = default)
    {
        await _db.Objectives.AddAsync(objective, ct);
    }
}
