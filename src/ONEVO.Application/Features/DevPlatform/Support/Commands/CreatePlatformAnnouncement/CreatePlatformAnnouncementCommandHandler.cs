using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.CreatePlatformAnnouncement;

public sealed class CreatePlatformAnnouncementCommandHandler
    : IRequestHandler<CreatePlatformAnnouncementCommand, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _announcements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreatePlatformAnnouncementCommandHandler(
        IPlatformAnnouncementRepository announcements,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _announcements = announcements;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        CreatePlatformAnnouncementCommand request,
        CancellationToken ct)
    {
        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Result<PlatformAnnouncementDto>.Failure("title is required and must be at most 200 characters.");
        }

        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000)
        {
            return Result<PlatformAnnouncementDto>.Failure("body is required and must be at most 4000 characters.");
        }

        var severity = string.IsNullOrWhiteSpace(request.Severity)
            ? PlatformAnnouncement.SeverityInfo
            : request.Severity.Trim();
        if (!PlatformAnnouncement.AllSeverities.Contains(severity))
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"severity must be one of: {string.Join(", ", PlatformAnnouncement.AllSeverities)}.");
        }

        var audience = string.IsNullOrWhiteSpace(request.Audience)
            ? PlatformAnnouncement.AudienceAll
            : request.Audience.Trim();
        if (!PlatformAnnouncement.AllAudiences.Contains(audience))
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"audience must be one of: {string.Join(", ", PlatformAnnouncement.AllAudiences)}.");
        }

        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = title,
            Body = body,
            Severity = severity,
            Audience = audience,
            IsPublished = false,
            CreatedAt = _clock.UtcNow,
        };

        await _announcements.AddAsync(announcement, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<PlatformAnnouncementDto>.Success(PlatformAnnouncementMapper.ToDto(announcement));
    }
}
