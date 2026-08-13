using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public sealed class EfEmploymentTypeRepository : IEmploymentTypeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmploymentTypeRepository(ApplicationDbContext db) => _db = db;

    public async Task<int?> GetIdByCodeAsync(string code, CancellationToken ct = default)
    {
        var match = await _db.EmploymentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code, ct);
        return match?.Id;
    }
}
