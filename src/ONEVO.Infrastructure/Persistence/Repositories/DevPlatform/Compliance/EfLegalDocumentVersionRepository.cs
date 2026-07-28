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

    public async Task<IReadOnlyList<LegalDocumentVersion>> ListAsync(
        string? documentType, string? status, CancellationToken ct = default)
    {
        var query = _db.LegalDocumentVersions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            query = query.Where(x => x.DocumentType == documentType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var results = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return results;
    }

    public async Task<LegalDocumentVersion?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return entity;
    }

    public async Task<LegalDocumentVersion?> GetPublishedAsync(
        string documentType, string version, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.DocumentType == documentType && x.Version == version && x.Status == "published",
                ct);

        return entity;
    }

    public async Task<LegalDocumentVersion?> GetCurrentPublishedByDocumentTypeAsync(
        string documentType, CancellationToken ct = default)
    {
        var entity = await _db.LegalDocumentVersions
            .FirstOrDefaultAsync(x => x.DocumentType == documentType && x.Status == "published", ct);

        return entity;
    }
}
