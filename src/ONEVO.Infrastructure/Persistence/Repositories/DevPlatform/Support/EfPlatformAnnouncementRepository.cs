using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support;

public sealed class EfPlatformAnnouncementRepository : IPlatformAnnouncementRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformAnnouncementRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PlatformAnnouncement?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var announcement = await _db.PlatformAnnouncements.FirstOrDefaultAsync(a => a.Id == id, ct);
        return announcement;
    }

    public async Task<IReadOnlyList<PlatformAnnouncement>> ListAsync(
        bool? isPublished,
        string? severity,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(isPublished, severity);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return items;
    }

    public async Task<int> CountAsync(bool? isPublished, string? severity, CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(isPublished, severity);
        var count = await query.CountAsync(ct);
        return count;
    }

    public async Task AddAsync(PlatformAnnouncement announcement, CancellationToken ct = default)
    {
        await _db.PlatformAnnouncements.AddAsync(announcement, ct);
    }

    private IQueryable<PlatformAnnouncement> BuildFilteredQuery(bool? isPublished, string? severity)
    {
        var query = _db.PlatformAnnouncements.AsNoTracking().AsQueryable();

        if (isPublished.HasValue)
        {
            query = query.Where(a => a.IsPublished == isPublished.Value);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(a => a.Severity == severity);
        }

        return query;
    }
}
