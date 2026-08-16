using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.PublishPlatformAnnouncement;

public sealed class PublishPlatformAnnouncementCommandHandler
    : IRequestHandler<PublishPlatformAnnouncementCommand, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _announcements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public PublishPlatformAnnouncementCommandHandler(
        IPlatformAnnouncementRepository announcements,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _announcements = announcements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        PublishPlatformAnnouncementCommand request,
        CancellationToken ct)
    {
        var announcement = await _announcements.GetByIdAsync(request.AnnouncementId, ct);
        if (announcement is null)
        {
            return Result<PlatformAnnouncementDto>.NotFound("Platform announcement not found.");
        }

        if (announcement.PublishedAt is null)
        {
            announcement.PublishedAt = _clock.UtcNow;
        }

        announcement.IsPublished = true;
        announcement.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<PlatformAnnouncementDto>.Success(PlatformAnnouncementMapper.ToDto(announcement));
    }
}
