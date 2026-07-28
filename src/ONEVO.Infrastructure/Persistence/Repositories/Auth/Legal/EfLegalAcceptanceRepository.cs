using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Legal;

public class EfLegalAcceptanceRepository : ILegalAcceptanceRepository
{
    private readonly ApplicationDbContext _db;

    public EfLegalAcceptanceRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LegalAcceptanceRecord>> GetUserAcceptancesAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.LegalAcceptanceRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .OrderByDescending(x => x.DecidedAt)
            .ToListAsync(ct);
    }

    public async Task<LegalAcceptanceRecord?> GetUserAcceptanceForDocumentAsync(Guid tenantId, Guid userId, string documentType, string version, CancellationToken ct = default)
    {
        return await _db.LegalAcceptanceRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DocumentType == documentType && x.DocumentVersion == version, ct);
    }

    public async Task AddAsync(LegalAcceptanceRecord record, CancellationToken ct = default)
    {
        await _db.LegalAcceptanceRecords.AddAsync(record, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
}
