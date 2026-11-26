using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Application.Features.DevPlatform.Support.Validation;

namespace ONEVO.Application.Features.DevPlatform.Support.Tenant;

// Every handler here scopes to ICurrentUser.TenantId. No tenant id is ever read from the request, and
// internal notes and internal files are never returned or accepted.

public sealed record ListTenantSupportTicketsQuery(string? Status, int? Page, int? PageSize)
    : IRequest<Result<SupportPageDto<TenantSupportTicketDto>>>;

public sealed class ListTenantSupportTicketsQueryHandler
    : IRequestHandler<ListTenantSupportTicketsQuery, Result<SupportPageDto<TenantSupportTicketDto>>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ICurrentUser _currentUser;

    public ListTenantSupportTicketsQueryHandler(ISupportTicketRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<SupportPageDto<TenantSupportTicketDto>>> Handle(
        ListTenantSupportTicketsQuery request, CancellationToken cancellationToken)
    {
        if (request.Status is not null && !SupportTicket.Statuses.Contains(request.Status))
        {
            return Result<SupportPageDto<TenantSupportTicketDto>>.Failure(
                $"status must be one of: {string.Join(", ", SupportTicket.Statuses)}.");
        }

        var (page, pageSize) = SupportTicketRules.NormalizePaging(request.Page, request.PageSize);
        var (items, total) = await _repository.ListAsync(
            new SupportTicketFilter(_currentUser.TenantId, request.Status, null, null, false, null, page, pageSize),
            cancellationToken);

        return Result<SupportPageDto<TenantSupportTicketDto>>.Success(new SupportPageDto<TenantSupportTicketDto>(
            items.Select(r => SupportMapper.ToTenantDto(r.Ticket)).ToList(), total, page, pageSize));
    }
}

public sealed record GetTenantSupportTicketQuery(Guid TicketId) : IRequest<Result<TenantSupportTicketDetailDto>>;

public sealed class GetTenantSupportTicketQueryHandler
    : IRequestHandler<GetTenantSupportTicketQuery, Result<TenantSupportTicketDetailDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetTenantSupportTicketQueryHandler(ISupportTicketRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<TenantSupportTicketDetailDto>> Handle(
        GetTenantSupportTicketQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _repository.GetWithChildrenAsync(request.TicketId, _currentUser.TenantId, cancellationToken);
        return ticket is null
            ? Result<TenantSupportTicketDetailDto>.NotFound($"Support ticket '{request.TicketId}' was not found.")
            : Result<TenantSupportTicketDetailDto>.Success(SupportMapper.ToTenantDetailDto(ticket));
    }
}

/// <summary>
/// <paramref name="CorrelationIds"/>: references of the user's recent failed requests (DEC-114), stored on the
/// ticket so support staff can search Application Insights. They are never copied into an audit row.
/// </summary>
public sealed record CreateTenantSupportTicketCommand(
    string? Subject, string? Description, string? Priority, string? Category,
    IReadOnlyList<string?>? CorrelationIds = null)
    : IRequest<Result<TenantSupportTicketDto>>, ISupportTicketInput;

public sealed class CreateTenantSupportTicketCommandHandler
    : IRequestHandler<CreateTenantSupportTicketCommand, Result<TenantSupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateTenantSupportTicketCommandHandler(
        ISupportTicketRepository repository,
        ISupportNotifier notifier,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _notifier = notifier;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantSupportTicketDto>> Handle(
        CreateTenantSupportTicketCommand request, CancellationToken cancellationToken)
    {
        var error = SupportTicketRules.ValidateTicket(request.Subject, request.Description, request.Priority, request.Category)
            ?? SupportTicketRules.ValidateCorrelationIds(request.CorrelationIds);
        if (error is not null) return Result<TenantSupportTicketDto>.Failure(error);

        var name = await TenantActor.NameAsync(_repository, _currentUser, cancellationToken);

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            Subject = request.Subject!.Trim(),
            Description = request.Description!.Trim(),
            Status = SupportTicket.StatusOpen,
            Priority = request.Priority!,
            Category = SupportTicketRules.NormalizeCategory(request.Category),
            CreatedByUserId = _currentUser.UserId,
            CreatedByName = name,
            AwaitingSupport = true,
            CorrelationIds = SupportTicketRules.JoinCorrelationIds(request.CorrelationIds),
            CreatedAt = _clock.UtcNow,
        };

        await _repository.AddAsync(ticket, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.AlertSupportAsync(ticket, SupportAlertKind.TicketRaised, name, ticket.Description, cancellationToken);
        return Result<TenantSupportTicketDto>.Success(SupportMapper.ToTenantDto(ticket));
    }
}

/// <summary>
/// The tenant corrects their own ticket while support is still working on it (open or in progress). Priority can
/// change too: a ticket that became urgent should say so.
/// </summary>
public sealed record UpdateTenantSupportTicketCommand(
    Guid TicketId, string? Subject, string? Description, string? Priority, string? Category)
    : IRequest<Result<TenantSupportTicketDto>>, ISupportTicketInput;

public sealed class UpdateTenantSupportTicketCommandHandler
    : IRequestHandler<UpdateTenantSupportTicketCommand, Result<TenantSupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateTenantSupportTicketCommandHandler(
        ISupportTicketRepository repository, ICurrentUser currentUser, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantSupportTicketDto>> Handle(
        UpdateTenantSupportTicketCommand request, CancellationToken cancellationToken)
    {
        var error = SupportTicketRules.ValidateTicket(request.Subject, request.Description, request.Priority, request.Category);
        if (error is not null) return Result<TenantSupportTicketDto>.Failure(error);

        var ticket = await _repository.GetAsync(request.TicketId, _currentUser.TenantId, cancellationToken);
        if (ticket is null) return Result<TenantSupportTicketDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");
        if (SupportTicketRules.IsFinished(ticket.Status))
        {
            return Result<TenantSupportTicketDto>.Conflict("A resolved or closed ticket can no longer be edited.");
        }

        SupportTicketRules.ApplyEdit(ticket, request.Subject!, request.Description!, request.Priority!, request.Category, _clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<TenantSupportTicketDto>.Success(SupportMapper.ToTenantDto(ticket));
    }
}

/// <summary>
/// A tenant reply on a resolved ticket reopens it. A closed ticket takes no more replies: the tenant raises a
/// new ticket instead.
/// </summary>
public sealed record AddTenantSupportTicketCommentCommand(Guid TicketId, string? Body)
    : IRequest<Result<TenantSupportTicketCommentDto>>;

public sealed class AddTenantSupportTicketCommentCommandHandler
    : IRequestHandler<AddTenantSupportTicketCommentCommand, Result<TenantSupportTicketCommentDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AddTenantSupportTicketCommentCommandHandler(
        ISupportTicketRepository repository,
        ISupportNotifier notifier,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _notifier = notifier;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantSupportTicketCommentDto>> Handle(
        AddTenantSupportTicketCommentCommand request, CancellationToken cancellationToken)
    {
        var error = SupportTicketRules.ValidateComment(request.Body);
        if (error is not null) return Result<TenantSupportTicketCommentDto>.Failure(error);

        var ticket = await _repository.GetAsync(request.TicketId, _currentUser.TenantId, cancellationToken);
        if (ticket is null) return Result<TenantSupportTicketCommentDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");
        if (ticket.Status == SupportTicket.StatusClosed)
        {
            return Result<TenantSupportTicketCommentDto>.Conflict("This ticket is closed. Raise a new ticket instead.");
        }

        var now = _clock.UtcNow;
        var comment = new SupportTicketComment
        {
            Id = Guid.NewGuid(),
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            AuthorUserId = _currentUser.UserId,
            AuthorName = await TenantActor.NameAsync(_repository, _currentUser, cancellationToken),
            Body = request.Body!.Trim(),
            IsInternal = false,
            CreatedAt = now,
        };

        await _repository.AddCommentAsync(comment, cancellationToken);
        if (ticket.Status == SupportTicket.StatusResolved)
        {
            SupportTicketRules.ApplyStatus(ticket, SupportTicket.StatusOpen, now);
        }

        ticket.UpdatedAt = now;
        ticket.AwaitingSupport = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.AlertSupportAsync(ticket, SupportAlertKind.TenantReplied, comment.AuthorName, comment.Body, cancellationToken);
        return Result<TenantSupportTicketCommentDto>.Success(SupportMapper.ToTenantDto(comment));
    }
}

public sealed record UploadTenantSupportTicketAttachmentCommand(Guid TicketId, SupportUpload Upload)
    : IRequest<Result<TenantSupportTicketAttachmentDto>>;

public sealed class UploadTenantSupportTicketAttachmentCommandHandler
    : IRequestHandler<UploadTenantSupportTicketAttachmentCommand, Result<TenantSupportTicketAttachmentDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportAttachmentService _attachments;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UploadTenantSupportTicketAttachmentCommandHandler(
        ISupportTicketRepository repository,
        ISupportAttachmentService attachments,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _attachments = attachments;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantSupportTicketAttachmentDto>> Handle(
        UploadTenantSupportTicketAttachmentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _repository.GetAsync(request.TicketId, _currentUser.TenantId, cancellationToken);
        if (ticket is null) return Result<TenantSupportTicketAttachmentDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");
        if (ticket.Status == SupportTicket.StatusClosed)
        {
            return Result<TenantSupportTicketAttachmentDto>.Conflict("This ticket is closed. Raise a new ticket instead.");
        }

        var name = await TenantActor.NameAsync(_repository, _currentUser, cancellationToken);
        var stored = await SupportAttachmentFlow.StoreAsync(
            _repository, _attachments, _unitOfWork, _clock, ticket, request.Upload,
            new SupportUploader(null, _currentUser.UserId, name), isInternal: false, cancellationToken);

        return stored.IsSuccess
            ? Result<TenantSupportTicketAttachmentDto>.Success(SupportMapper.ToTenantDto(stored.Value!))
            : Result<TenantSupportTicketAttachmentDto>.Failure(stored.Error!, stored.StatusCode ?? 400, stored.ErrorCode);
    }
}

public sealed record GetTenantSupportAttachmentDownloadUrlQuery(Guid TicketId, Guid AttachmentId)
    : IRequest<Result<SupportAttachmentDownloadDto>>;

public sealed class GetTenantSupportAttachmentDownloadUrlQueryHandler
    : IRequestHandler<GetTenantSupportAttachmentDownloadUrlQuery, Result<SupportAttachmentDownloadDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportAttachmentService _attachments;
    private readonly ICurrentUser _currentUser;

    public GetTenantSupportAttachmentDownloadUrlQueryHandler(
        ISupportTicketRepository repository, ISupportAttachmentService attachments, ICurrentUser currentUser)
    {
        _repository = repository;
        _attachments = attachments;
        _currentUser = currentUser;
    }

    public async Task<Result<SupportAttachmentDownloadDto>> Handle(
        GetTenantSupportAttachmentDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentAsync(
            request.TicketId, request.AttachmentId, _currentUser.TenantId, cancellationToken);

        // An internal file answers exactly like a missing one.
        if (attachment is null || attachment.IsInternal)
        {
            return Result<SupportAttachmentDownloadDto>.NotFound($"Attachment '{request.AttachmentId}' was not found.");
        }

        return Result<SupportAttachmentDownloadDto>.Success(await _attachments.GetDownloadUrlAsync(attachment, cancellationToken));
    }
}

internal static class TenantActor
{
    public static async Task<string> NameAsync(ISupportTicketRepository repository, ICurrentUser currentUser, CancellationToken ct) =>
        await repository.GetTenantUserNameAsync(currentUser.UserId, ct) ?? currentUser.Email;
}
