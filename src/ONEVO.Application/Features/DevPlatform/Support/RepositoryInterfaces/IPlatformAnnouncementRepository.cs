using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

public interface IPlatformAnnouncementRepository
{
    Task<(IReadOnlyList<PlatformAnnouncement> Items, int Total)> ListAsync(
        bool? isPublished, string? severity, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Published announcements whose audience includes the given one and whose display window contains
    /// <paramref name="now"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<PlatformAnnouncement>> ListActiveAsync(
        string audience, DateTimeOffset now, int take, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<PlatformAnnouncement?> GetAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(PlatformAnnouncement announcement, CancellationToken ct = default);

    void Remove(PlatformAnnouncement announcement);
}
