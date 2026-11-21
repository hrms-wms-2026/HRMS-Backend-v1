using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Application.Features.DevPlatform.Support.Validation;

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

/// <summary>One announcement, for the admin edit screen.</summary>
public sealed record GetPlatformAnnouncementQuery(Guid Id) : IRequest<Result<PlatformAnnouncementDto>>;

public sealed class GetPlatformAnnouncementQueryHandler
    : IRequestHandler<GetPlatformAnnouncementQuery, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _repository;

    public GetPlatformAnnouncementQueryHandler(IPlatformAnnouncementRepository repository) => _repository = repository;

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        GetPlatformAnnouncementQuery request, CancellationToken cancellationToken)
    {
        var announcement = await _repository.GetAsync(request.Id, cancellationToken);
        return announcement is null
            ? Result<PlatformAnnouncementDto>.NotFound($"Announcement '{request.Id}' was not found.")
            : Result<PlatformAnnouncementDto>.Success(SupportMapper.ToDto(announcement));
    }
}

/// <summary>Creates a draft announcement. Publishing is a separate step.</summary>
public sealed record CreatePlatformAnnouncementCommand(
    string? Title,
    string? Body,
    string? Severity,
    string? Audience,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformAnnouncementDto>>, IAuditedCommand<Result<PlatformAnnouncementDto>>, IPlatformAnnouncementInput
{
    // Title and body are left out; the announcement row keeps them.
    public AuditEntry DescribeAudit(Result<PlatformAnnouncementDto>? response)
        => AuditEntry.For("announcement.created", "announcement", response?.Value?.Id, null,
            new { Severity, Audience, StartsAt, EndsAt });
}

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
        var error = AnnouncementRules.Validate(
            request.Title, request.Body, request.Severity, request.Audience, request.StartsAt, request.EndsAt);
        if (error is not null) return Result<PlatformAnnouncementDto>.Failure(error);

        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = request.Title!.Trim(),
            Body = request.Body!.Trim(),
            Severity = request.Severity!,
            Audience = request.Audience!,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            IsPublished = false,
            CreatedByPlatformUserId = request.ActorPlatformUserId,
            CreatedAt = _clock.UtcNow,
        };

        await _repository.AddAsync(announcement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<PlatformAnnouncementDto>.Success(SupportMapper.ToDto(announcement));
    }
}

/// <summary>
/// Replaces an announcement's text, severity, audience and display window. Allowed whether or not it is
/// published: a published edit shows on the next banner load.
/// </summary>
public sealed record UpdatePlatformAnnouncementCommand(
    Guid Id,
    string? Title,
    string? Body,
    string? Severity,
    string? Audience,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt) : IRequest<Result<PlatformAnnouncementDto>>, IAuditedCommand<Result<PlatformAnnouncementDto>>, IPlatformAnnouncementInput
{
    // Title and body are left out; the announcement row keeps them.
    public AuditEntry DescribeAudit(Result<PlatformAnnouncementDto>? response)
        => AuditEntry.For("announcement.updated", "announcement", Id, null, new { Severity, Audience, StartsAt, EndsAt });
}

public sealed class UpdatePlatformAnnouncementCommandHandler
    : IRequestHandler<UpdatePlatformAnnouncementCommand, Result<PlatformAnnouncementDto>>
{
    private readonly IPlatformAnnouncementRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdatePlatformAnnouncementCommandHandler(
        IPlatformAnnouncementRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PlatformAnnouncementDto>> Handle(
        UpdatePlatformAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var error = AnnouncementRules.Validate(
            request.Title, request.Body, request.Severity, request.Audience, request.StartsAt, request.EndsAt);
        if (error is not null) return Result<PlatformAnnouncementDto>.Failure(error);

        var announcement = await _repository.GetAsync(request.Id, cancellationToken);
        if (announcement is null) return Result<PlatformAnnouncementDto>.NotFound($"Announcement '{request.Id}' was not found.");

        announcement.Title = request.Title!.Trim();
        announcement.Body = request.Body!.Trim();
        announcement.Severity = request.Severity!;
        announcement.Audience = request.Audience!;
        announcement.StartsAt = request.StartsAt;
        announcement.EndsAt = request.EndsAt;
        announcement.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PlatformAnnouncementDto>.Success(SupportMapper.ToDto(announcement));
    }
}

/// <summary>Permanently deletes an announcement. Unpublish instead to keep it for later.</summary>
public sealed record DeletePlatformAnnouncementCommand(Guid Id) : IRequest<Result<bool>>, IAuditedCommand<Result<bool>>
{
    public AuditEntry DescribeAudit(Result<bool>? response) => AuditEntry.For("announcement.deleted", "announcement", Id);
}

public sealed class DeletePlatformAnnouncementCommandHandler : IRequestHandler<DeletePlatformAnnouncementCommand, Result<bool>>
{
    private readonly IPlatformAnnouncementRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePlatformAnnouncementCommandHandler(IPlatformAnnouncementRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeletePlatformAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var announcement = await _repository.GetAsync(request.Id, cancellationToken);
        if (announcement is null) return Result<bool>.NotFound($"Announcement '{request.Id}' was not found.");

        _repository.Remove(announcement);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}

public sealed record SetPlatformAnnouncementPublishedCommand(Guid Id, bool Publish, Guid ActorPlatformUserId)
    : IRequest<Result<PlatformAnnouncementDto>>, IAuditedCommand<Result<PlatformAnnouncementDto>>
{
    public AuditEntry DescribeAudit(Result<PlatformAnnouncementDto>? response)
        => AuditEntry.For(Publish ? "announcement.published" : "announcement.unpublished", "announcement", Id);
}

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
/// from the client. Only announcements inside their display window are returned.
/// </summary>
public sealed record GetActiveAnnouncementsQuery(string Audience) : IRequest<Result<IReadOnlyList<ActiveAnnouncementDto>>>;

public sealed class GetActiveAnnouncementsQueryHandler
    : IRequestHandler<GetActiveAnnouncementsQuery, Result<IReadOnlyList<ActiveAnnouncementDto>>>
{
    private const int MaxBanners = 5;
    private readonly IPlatformAnnouncementRepository _repository;
    private readonly IDateTimeProvider _clock;

    public GetActiveAnnouncementsQueryHandler(IPlatformAnnouncementRepository repository, IDateTimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ActiveAnnouncementDto>>> Handle(
        GetActiveAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        if (request.Audience is not (PlatformAnnouncement.AudienceTenants or PlatformAnnouncement.AudiencePlatformAdmins))
        {
            return Result<IReadOnlyList<ActiveAnnouncementDto>>.Failure("Unknown announcement audience.");
        }

        var items = await _repository.ListActiveAsync(request.Audience, _clock.UtcNow, MaxBanners, cancellationToken);
        IReadOnlyList<ActiveAnnouncementDto> dtos = items.Select(SupportMapper.ToActiveDto).ToList();
        return Result<IReadOnlyList<ActiveAnnouncementDto>>.Success(dtos);
    }
}

internal static class AnnouncementRules
{
    public static string? Validate(
        string? title, string? body, string? severity, string? audience, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
    {
        if (string.IsNullOrWhiteSpace(title)) return "title is required.";
        if (title.Trim().Length > SupportTicketRules.AnnouncementTitleMaxLength)
            return $"title must be at most {SupportTicketRules.AnnouncementTitleMaxLength} characters.";
        if (string.IsNullOrWhiteSpace(body)) return "body is required.";
        if (body.Trim().Length > SupportTicketRules.AnnouncementBodyMaxLength)
            return $"body must be at most {SupportTicketRules.AnnouncementBodyMaxLength} characters.";

        var activeContent = SupportTicketRules.ValidateAnnouncementText(title, body);
        if (activeContent is not null) return activeContent;

        if (!PlatformAnnouncement.Severities.Contains(severity ?? string.Empty))
            return $"severity must be one of: {string.Join(", ", PlatformAnnouncement.Severities)}.";
        if (!PlatformAnnouncement.Audiences.Contains(audience ?? string.Empty))
            return $"audience must be one of: {string.Join(", ", PlatformAnnouncement.Audiences)}.";
        if (startsAt is { } start && endsAt is { } end && end <= start)
            return "ends_at must be after starts_at.";
        return null;
    }
}
