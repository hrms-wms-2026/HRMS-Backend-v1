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
}
