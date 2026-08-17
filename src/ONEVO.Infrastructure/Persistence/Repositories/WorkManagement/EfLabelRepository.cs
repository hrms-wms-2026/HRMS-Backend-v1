using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfLabelRepository : ILabelRepository
{
    private readonly ApplicationDbContext _db;

    public EfLabelRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Label label, CancellationToken ct = default)
    {
        await _db.Labels.AddAsync(label, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Label>>> GetByProjectIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, int takePerProject, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Label>>();

        var labels = await _db.Labels.AsNoTracking()
            .Where(l => l.TenantId == tenantId && projectIds.Contains(l.ProjectId))
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        return labels
            .GroupBy(l => l.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Label>)g.Take(takePerProject).ToList());
    }
}
