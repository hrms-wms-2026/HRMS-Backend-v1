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

    public async Task<string?> GetCodeByIdAsync(int id, CancellationToken ct = default)
    {
        var match = await _db.EmploymentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        return match?.Code;
    }
}
