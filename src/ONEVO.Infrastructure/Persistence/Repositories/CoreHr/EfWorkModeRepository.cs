using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public sealed class EfWorkModeRepository : IWorkModeRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkModeRepository(ApplicationDbContext db) => _db = db;

    public Task<bool> ExistsActiveAsync(int workModeId, CancellationToken ct = default) =>
        _db.WorkModes.AsNoTracking().AnyAsync(w => w.Id == workModeId && w.IsActive, ct);

    public Task<List<WorkMode>> ListActiveAsync(CancellationToken ct = default) =>
        _db.WorkModes.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Label)
            .ToListAsync(ct);
}
