using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support;

public sealed class EfPlatformAnnouncementRepository : IPlatformAnnouncementRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformAnnouncementRepository(ApplicationDbContext db) => _db = db;

    public async Task<(IReadOnlyList<PlatformAnnouncement> Items, int Total)> ListAsync(
        bool? isPublished, string? severity, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.PlatformAnnouncements.AsNoTracking().AsQueryable();
        if (isPublished is { } published) query = query.Where(a => a.IsPublished == published);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(a => a.Severity == severity);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<PlatformAnnouncement>> ListActiveAsync(
        string audience, int take, CancellationToken ct = default) =>
        await _db.PlatformAnnouncements.AsNoTracking()
            .Where(a => a.IsPublished)
            .Where(a => a.Audience == PlatformAnnouncement.AudienceAll || a.Audience == audience)
            .OrderByDescending(a => a.PublishedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<PlatformAnnouncement?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformAnnouncements.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(PlatformAnnouncement announcement, CancellationToken ct = default) =>
        await _db.PlatformAnnouncements.AddAsync(announcement, ct);
}
