using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Announcements;

public sealed record ListPlatformAnnouncementsQuery(bool? IsPublished, string? Severity, int? Page, int? PageSize)
    : IRequest<Result<SupportPageDto<PlatformAnnouncementDto>>>;

public sealed class ListPlatformAnnouncementsQueryHandler
    : IRequestHandler<ListPlatformAnnouncementsQuery, Result<SupportPageDto<PlatformAnnouncementDto>>>
{
    private readonly IPlatformAnnouncementRepository _repository;

    public ListPlatformAnnouncementsQueryHandler(IPlatformAnnouncementRepository repository) => _repository = repository;

    public async Task<Result<SupportPageDto<PlatformAnnouncementDto>>> Handle(
        ListPlatformAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        if (request.Severity is not null && !PlatformAnnouncement.Severities.Contains(request.Severity))
        {
            return Result<SupportPageDto<PlatformAnnouncementDto>>.Failure(
                $"severity must be one of: {string.Join(", ", PlatformAnnouncement.Severities)}.");
        }

        var (page, pageSize) = SupportTicketRules.NormalizePaging(request.Page, request.PageSize);
        var (items, total) = await _repository.ListAsync(request.IsPublished, request.Severity, page, pageSize, cancellationToken);
        return Result<SupportPageDto<PlatformAnnouncementDto>>.Success(
            new SupportPageDto<PlatformAnnouncementDto>(items.Select(SupportMapper.ToDto).ToList(), total, page, pageSize));
    }
}

/// <summary>Creates a draft announcement. Publishing is a separate step.</summary>
public sealed record CreatePlatformAnnouncementCommand(
    string? Title,
    string? Body,
    string? Severity,
    string? Audience,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformAnnouncementDto>>;

public sealed class CreatePlatformAnnouncementCommandHandler
    : IRequestHandler<CreatePlatformAnnouncementCommand, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreatePlatformAnnouncementCommandHandler(
        IPlatformAnnouncementRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        CreatePlatformAnnouncementCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return Result<PlatformAnnouncementDto>.Failure("title is required.");
        if (request.Title.Trim().Length > SupportTicketRules.AnnouncementTitleMaxLength)
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"title must be at most {SupportTicketRules.AnnouncementTitleMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Body)) return Result<PlatformAnnouncementDto>.Failure("body is required.");
        if (request.Body.Trim().Length > SupportTicketRules.AnnouncementBodyMaxLength)
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"body must be at most {SupportTicketRules.AnnouncementBodyMaxLength} characters.");
        }

        var activeContent = SupportTicketRules.ValidateAnnouncementText(request.Title, request.Body);
        if (activeContent is not null) return Result<PlatformAnnouncementDto>.Failure(activeContent);

        if (!PlatformAnnouncement.Severities.Contains(request.Severity ?? string.Empty))
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"severity must be one of: {string.Join(", ", PlatformAnnouncement.Severities)}.");
        }

        if (!PlatformAnnouncement.Audiences.Contains(request.Audience ?? string.Empty))
        {
            return Result<PlatformAnnouncementDto>.Failure(
                $"audience must be one of: {string.Join(", ", PlatformAnnouncement.Audiences)}.");
        }

        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Severity = request.Severity!,
            Audience = request.Audience!,
            IsPublished = false,
            CreatedByPlatformUserId = request.ActorPlatformUserId,
            CreatedAt = _clock.UtcNow,
        };

        await _repository.AddAsync(announcement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<PlatformAnnouncementDto>.Success(SupportMapper.ToDto(announcement));
    }
}

public sealed record SetPlatformAnnouncementPublishedCommand(Guid Id, bool Publish, Guid ActorPlatformUserId)
    : IRequest<Result<PlatformAnnouncementDto>>;

public sealed class SetPlatformAnnouncementPublishedCommandHandler
    : IRequestHandler<SetPlatformAnnouncementPublishedCommand, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public SetPlatformAnnouncementPublishedCommandHandler(
        IPlatformAnnouncementRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        SetPlatformAnnouncementPublishedCommand request, CancellationToken cancellationToken)
    {
        var announcement = await _repository.GetAsync(request.Id, cancellationToken);
        if (announcement is null) return Result<PlatformAnnouncementDto>.NotFound($"Announcement '{request.Id}' was not found.");

        if (announcement.IsPublished != request.Publish)
        {
            var now = _clock.UtcNow;
            announcement.IsPublished = request.Publish;
            if (request.Publish)
            {
                announcement.PublishedAt = now;
                announcement.PublishedByPlatformUserId = request.ActorPlatformUserId;
            }

            announcement.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<PlatformAnnouncementDto>.Success(SupportMapper.ToDto(announcement));
    }
}

/// <summary>
/// Banner feed. The audience is fixed by the calling controller (admin console or tenant app), never taken
/// from the client.
/// </summary>
public sealed record GetActiveAnnouncementsQuery(string Audience) : IRequest<Result<IReadOnlyList<ActiveAnnouncementDto>>>;

public sealed class GetActiveAnnouncementsQueryHandler
    : IRequestHandler<GetActiveAnnouncementsQuery, Result<IReadOnlyList<ActiveAnnouncementDto>>>
{
    private const int MaxBanners = 5;
    private readonly IPlatformAnnouncementRepository _repository;

    public GetActiveAnnouncementsQueryHandler(IPlatformAnnouncementRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ActiveAnnouncementDto>>> Handle(
        GetActiveAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        if (request.Audience is not (PlatformAnnouncement.AudienceTenants or PlatformAnnouncement.AudiencePlatformAdmins))
        {
            return Result<IReadOnlyList<ActiveAnnouncementDto>>.Failure("Unknown announcement audience.");
        }

        var items = await _repository.ListActiveAsync(request.Audience, MaxBanners, cancellationToken);
        IReadOnlyList<ActiveAnnouncementDto> dtos = items.Select(SupportMapper.ToActiveDto).ToList();
        return Result<IReadOnlyList<ActiveAnnouncementDto>>.Success(dtos);
    }
}
