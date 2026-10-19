using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public sealed class EfNotificationTemplateStore : INotificationTemplateStore
{
    private readonly ApplicationDbContext _db;

    public EfNotificationTemplateStore(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(string? eventKey, CancellationToken ct = default)
        => await _db.NotificationTemplates.AsNoTracking()
            .Where(t => eventKey == null || t.EventKey == eventKey)
            .OrderBy(t => t.EventKey).ThenBy(t => t.Name)
            .ToListAsync(ct);

    public Task<NotificationTemplate?> GetTrackedAsync(Guid templateId, CancellationToken ct = default)
        => _db.NotificationTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => _db.NotificationTemplates.AnyAsync(t => t.Code == code, ct);

    public Task<NotificationTemplate?> GetDefaultForEventAsync(string eventKey, CancellationToken ct = default)
        => _db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Code == eventKey && t.EventKey == eventKey, ct);

    public async Task<IReadOnlyList<NotificationTemplateVersion>> ListVersionsAsync(Guid templateId, CancellationToken ct = default)
        => await _db.NotificationTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

    public Task<NotificationTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
        => _db.NotificationTemplateVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);

    public Task<NotificationTemplateVersion?> GetTrackedDraftAsync(Guid templateId, CancellationToken ct = default)
        => _db.NotificationTemplateVersions.FirstOrDefaultAsync(
            v => v.TemplateId == templateId && v.Status == NotificationTemplateVersionStatus.Draft, ct);

    public async Task<int> GetLatestVersionNumberAsync(Guid templateId, CancellationToken ct = default)
        => await _db.NotificationTemplateVersions.Where(v => v.TemplateId == templateId)
            .Select(v => (int?)v.Version).MaxAsync(ct) ?? 0;

    public async Task AddTemplateAsync(NotificationTemplate template, CancellationToken ct = default)
        => await _db.NotificationTemplates.AddAsync(template, ct);

    public async Task AddVersionAsync(NotificationTemplateVersion version, CancellationToken ct = default)
        => await _db.NotificationTemplateVersions.AddAsync(version, ct);
}
