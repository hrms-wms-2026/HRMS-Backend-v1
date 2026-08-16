using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

public interface IPlatformAnnouncementRepository
{
    Task<PlatformAnnouncement?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PlatformAnnouncement>> ListAsync(
        bool? isPublished,
        string? severity,
        int skip,
        int take,
        CancellationToken ct = default);
    Task<int> CountAsync(bool? isPublished, string? severity, CancellationToken ct = default);
    Task AddAsync(PlatformAnnouncement announcement, CancellationToken ct = default);
}
