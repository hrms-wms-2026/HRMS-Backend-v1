using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Mappers;

internal static class PlatformAnnouncementMapper
{
    internal static PlatformAnnouncementDto ToDto(PlatformAnnouncement announcement) =>
        new(
            announcement.Id,
            announcement.Title,
            announcement.Body,
            announcement.Severity,
            announcement.Audience,
            announcement.IsPublished,
            announcement.PublishedAt,
            announcement.CreatedAt,
            announcement.UpdatedAt);

    internal static PlatformAnnouncementListResponseDto ToListResponseDto(
        IReadOnlyList<PlatformAnnouncementDto> items, int totalCount, int page, int pageSize) =>
        new(items, totalCount, page, pageSize);
}
