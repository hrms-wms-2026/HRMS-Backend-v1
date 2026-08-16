using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.ListPlatformAnnouncements;

public sealed class ListPlatformAnnouncementsQueryHandler
    : IRequestHandler<ListPlatformAnnouncementsQuery, Result<PlatformAnnouncementListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly IPlatformAnnouncementRepository _announcements;

    public ListPlatformAnnouncementsQueryHandler(IPlatformAnnouncementRepository announcements)
    {
        _announcements = announcements;
    }

    public async Task<Result<PlatformAnnouncementListResponseDto>> Handle(
        ListPlatformAnnouncementsQuery request,
        CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var skip = (page - 1) * size;

        var announcements = await _announcements.ListAsync(request.IsPublished, request.Severity, skip, size, ct);
        var total = await _announcements.CountAsync(request.IsPublished, request.Severity, ct);
        var items = announcements.Select(PlatformAnnouncementMapper.ToDto).ToList();

        return Result<PlatformAnnouncementListResponseDto>.Success(
            PlatformAnnouncementMapper.ToListResponseDto(items, total, page, size));
    }
}
