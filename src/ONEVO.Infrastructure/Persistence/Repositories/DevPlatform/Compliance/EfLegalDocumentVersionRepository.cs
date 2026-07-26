using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Compliance;

public class EfLegalDocumentVersionRepository : ILegalDocumentVersionRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfLegalDocumentVersionRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LegalDocumentVersion>> GetCurrentRequiredVersionsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var versions = await _db.LegalDocumentVersions
            .AsNoTracking()
            .Where(x => x.Status == "published")
            .Where(x => x.IsRequired)
            .Where(x => x.BlockScope == "dashboard")
            .Where(x => x.PublishedAt != null && x.PublishedAt <= now)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync(ct);

        return versions;
    }

    public async Task<LegalDocumentVersion?> GetByDocumentTypeAndVersionAsync(string documentType, string version, CancellationToken ct = default)
    {
        return await _db.LegalDocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentType == documentType && x.Version == version, ct);
    }

    public async Task AddAsync(LegalDocumentVersion entity, CancellationToken ct = default)
    {
        await _db.LegalDocumentVersions.AddAsync(entity, ct);
    }
}
