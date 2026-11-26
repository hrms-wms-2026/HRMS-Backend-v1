using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Application.Features.DevPlatform.Support.Validation;

namespace ONEVO.Application.Features.DevPlatform.Support.Admin;

/// <summary>Platform staff open a ticket on a tenant's behalf. Every ticket belongs to exactly one tenant.</summary>
public sealed record CreateSupportTicketCommand(
    Guid? TenantId,
    string? Subject,
    string? Description,
    string? Priority,
    string? Category,
    Guid ActorPlatformUserId) : IRequest<Result<SupportTicketDto>>, IAuditedCommand<Result<SupportTicketDto>>, ISupportTicketInput
{
    // Subject, description and category are customer text: never copied into the audit row.
    public AuditEntry DescribeAudit(Result<SupportTicketDto>? response)
        => AuditEntry.For("support_ticket.created", "support_ticket", response?.Value?.Id,
            TenantId ?? response?.Value?.TenantId, new { Priority });
}

public sealed class CreateSupportTicketCommandHandler : IRequestHandler<CreateSupportTicketCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateSupportTicketCommandHandler(ISupportTicketRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(CreateSupportTicketCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result<SupportTicketDto>.Failure("tenant_id is required.");
        }

        var error = SupportTicketRules.ValidateTicket(request.Subject, request.Description, request.Priority, request.Category);
        if (error is not null) return Result<SupportTicketDto>.Failure(error);

        var tenantName = await _repository.GetTenantNameAsync(tenantId, cancellationToken);
        if (tenantName is null) return Result<SupportTicketDto>.NotFound($"Tenant '{tenantId}' was not found.");

        var actorName = await _repository.GetPlatformUserNameAsync(request.ActorPlatformUserId, cancellationToken);
        if (actorName is null) return Result<SupportTicketDto>.Forbidden();

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Subject = request.Subject!.Trim(),
            Description = request.Description!.Trim(),
            Status = SupportTicket.StatusOpen,
            Priority = request.Priority!,
            Category = SupportTicketRules.NormalizeCategory(request.Category),
            CreatedByPlatformUserId = request.ActorPlatformUserId,
            CreatedByName = actorName,
            CreatedAt = _clock.UtcNow,
        };

        await _repository.AddAsync(ticket, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupportTicketDto>.Success(SupportMapper.ToDto(ticket, tenantName, null));
    }
}

public sealed record UpdateSupportTicketStatusCommand(Guid TicketId, string? Status)
    : IRequest<Result<SupportTicketDto>>, IAuditedCommand<Result<SupportTicketDto>>
{
    public AuditEntry DescribeAudit(Result<SupportTicketDto>? response)
        => AuditEntry.For("support_ticket.status_changed", "support_ticket", TicketId, response?.Value?.TenantId, new { Status });
}

public sealed class UpdateSupportTicketStatusCommandHandler
    : IRequestHandler<UpdateSupportTicketStatusCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateSupportTicketStatusCommandHandler(
        ISupportTicketRepository repository, ISupportNotifier notifier, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(UpdateSupportTicketStatusCommand request, CancellationToken cancellationToken)
    {
        if (request.Status is null || !SupportTicket.Statuses.Contains(request.Status))
        {
            return Result<SupportTicketDto>.Failure($"status must be one of: {string.Join(", ", SupportTicket.Statuses)}.");
        }

        var ticket = await _repository.GetAsync(request.TicketId, null, cancellationToken);
        if (ticket is null) return Result<SupportTicketDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");

        if (ticket.Status != request.Status)
        {
            SupportTicketRules.ApplyStatus(ticket, request.Status, _clock.UtcNow);

            // "open" is only reached by reopening; the tenant hears about progress, resolution and closure.
            if (request.Status != SupportTicket.StatusOpen)
            {
                await _notifier.QueueTenantStatusChangeAsync(ticket, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SupportTicketDto>.Success(await SupportTicketLabels.ToDtoAsync(_repository, ticket, cancellationToken));
    }
}

/// <summary>Assigns a ticket to an active platform user, or clears the assignee when null.</summary>
public sealed record AssignSupportTicketCommand(Guid TicketId, Guid? AssignedToPlatformUserId)
    : IRequest<Result<SupportTicketDto>>, IAuditedCommand<Result<SupportTicketDto>>
{
    public AuditEntry DescribeAudit(Result<SupportTicketDto>? response)
        => AuditEntry.For("support_ticket.assigned", "support_ticket", TicketId, response?.Value?.TenantId,
            new { AssignedToPlatformUserId });
}

public sealed class AssignSupportTicketCommandHandler : IRequestHandler<AssignSupportTicketCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AssignSupportTicketCommandHandler(ISupportTicketRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(AssignSupportTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _repository.GetAsync(request.TicketId, null, cancellationToken);
        if (ticket is null) return Result<SupportTicketDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");

        if (request.AssignedToPlatformUserId is { } assignee
            && await _repository.GetActivePlatformUserNameAsync(assignee, cancellationToken) is null)
        {
            return Result<SupportTicketDto>.Failure("assigned_to_platform_user_id must be an active platform user.");
        }

        if (ticket.AssignedToPlatformUserId != request.AssignedToPlatformUserId)
        {
            ticket.AssignedToPlatformUserId = request.AssignedToPlatformUserId;
            ticket.UpdatedAt = _clock.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SupportTicketDto>.Success(await SupportTicketLabels.ToDtoAsync(_repository, ticket, cancellationToken));
    }
}

public sealed record AddSupportTicketCommentCommand(Guid TicketId, string? Body, bool IsInternal, Guid ActorPlatformUserId)
    : IRequest<Result<SupportTicketCommentDto>>, IAuditedCommand<Result<SupportTicketCommentDto>>
{
    // The comment body is never copied into the audit row.
    public AuditEntry DescribeAudit(Result<SupportTicketCommentDto>? response)
        => AuditEntry.For("support_ticket.commented", "support_ticket", TicketId, null,
            new { CommentId = response?.Value?.Id, IsInternal });
}

public sealed class AddSupportTicketCommentCommandHandler
    : IRequestHandler<AddSupportTicketCommentCommand, Result<SupportTicketCommentDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportNotifier _notifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AddSupportTicketCommentCommandHandler(
        ISupportTicketRepository repository, ISupportNotifier notifier, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _notifier = notifier;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketCommentDto>> Handle(AddSupportTicketCommentCommand request, CancellationToken cancellationToken)
    {
        var error = SupportTicketRules.ValidateComment(request.Body);
        if (error is not null) return Result<SupportTicketCommentDto>.Failure(error);

        var ticket = await _repository.GetAsync(request.TicketId, null, cancellationToken);
        if (ticket is null) return Result<SupportTicketCommentDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");

        var actorName = await _repository.GetPlatformUserNameAsync(request.ActorPlatformUserId, cancellationToken);
        if (actorName is null) return Result<SupportTicketCommentDto>.Forbidden();

        var now = _clock.UtcNow;
        var comment = new SupportTicketComment
        {
            Id = Guid.NewGuid(),
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            AuthorPlatformUserId = request.ActorPlatformUserId,
            AuthorName = actorName,
            Body = request.Body!.Trim(),
            IsInternal = request.IsInternal,
            CreatedAt = now,
        };

        await _repository.AddCommentAsync(comment, cancellationToken);
        ticket.UpdatedAt = now;
        if (!comment.IsInternal)
        {
            // A reply the tenant can see answers them; an internal note does not.
            ticket.AwaitingSupport = false;
            await _notifier.QueueTenantReplyAsync(ticket, comment, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupportTicketCommentDto>.Success(SupportMapper.ToDto(comment));
    }
}

/// <summary>Platform staff correct a ticket's subject, description, priority or category.</summary>
public sealed record UpdateSupportTicketCommand(
    Guid TicketId,
    string? Subject,
    string? Description,
    string? Priority,
    string? Category) : IRequest<Result<SupportTicketDto>>, IAuditedCommand<Result<SupportTicketDto>>, ISupportTicketInput
{
    // Subject, description and category are customer text: never copied into the audit row.
    public AuditEntry DescribeAudit(Result<SupportTicketDto>? response)
        => AuditEntry.For("support_ticket.updated", "support_ticket", TicketId, response?.Value?.TenantId, new { Priority });
}

public sealed class UpdateSupportTicketCommandHandler : IRequestHandler<UpdateSupportTicketCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateSupportTicketCommandHandler(ISupportTicketRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(UpdateSupportTicketCommand request, CancellationToken cancellationToken)
    {
        var error = SupportTicketRules.ValidateTicket(request.Subject, request.Description, request.Priority, request.Category);
        if (error is not null) return Result<SupportTicketDto>.Failure(error);

        var ticket = await _repository.GetAsync(request.TicketId, null, cancellationToken);
        if (ticket is null) return Result<SupportTicketDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");

        SupportTicketRules.ApplyEdit(ticket, request.Subject!, request.Description!, request.Priority!, request.Category, _clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupportTicketDto>.Success(await SupportTicketLabels.ToDtoAsync(_repository, ticket, cancellationToken));
    }
}

public sealed record UploadSupportTicketAttachmentCommand(
    Guid TicketId,
    SupportUpload Upload,
    bool IsInternal,
    Guid ActorPlatformUserId) : IRequest<Result<SupportTicketAttachmentDto>>, IAuditedCommand<Result<SupportTicketAttachmentDto>>
{
    // The file name can carry personal data, so only the type and size are kept.
    public AuditEntry DescribeAudit(Result<SupportTicketAttachmentDto>? response)
        => AuditEntry.For("support_ticket.attachment_added", "support_ticket", TicketId, null,
            new { AttachmentId = response?.Value?.Id, IsInternal, ContentType = Upload?.ContentType, SizeBytes = Upload?.SizeBytes });
}

public sealed class UploadSupportTicketAttachmentCommandHandler
    : IRequestHandler<UploadSupportTicketAttachmentCommand, Result<SupportTicketAttachmentDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly ISupportAttachmentService _attachments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UploadSupportTicketAttachmentCommandHandler(
        ISupportTicketRepository repository,
        ISupportAttachmentService attachments,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _attachments = attachments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketAttachmentDto>> Handle(
        UploadSupportTicketAttachmentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _repository.GetAsync(request.TicketId, null, cancellationToken);
        if (ticket is null) return Result<SupportTicketAttachmentDto>.NotFound($"Support ticket '{request.TicketId}' was not found.");

        var actorName = await _repository.GetPlatformUserNameAsync(request.ActorPlatformUserId, cancellationToken);
        if (actorName is null) return Result<SupportTicketAttachmentDto>.Forbidden();

        var stored = await SupportAttachmentFlow.StoreAsync(
            _repository, _attachments, _unitOfWork, _clock, ticket, request.Upload,
            new SupportUploader(request.ActorPlatformUserId, null, actorName), request.IsInternal, cancellationToken);

        return stored.IsSuccess
            ? Result<SupportTicketAttachmentDto>.Success(SupportMapper.ToDto(stored.Value!))
            : Result<SupportTicketAttachmentDto>.Failure(stored.Error!, stored.StatusCode ?? 400, stored.ErrorCode);
    }
}

/// <summary>Shared by the admin and tenant upload handlers: cap check, store object, save row, clean up on failure.</summary>
internal static class SupportAttachmentFlow
{
    public static async Task<Result<SupportTicketAttachment>> StoreAsync(
        ISupportTicketRepository repository,
        ISupportAttachmentService attachments,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        SupportTicket ticket,
        SupportUpload upload,
        SupportUploader uploader,
        bool isInternal,
        CancellationToken ct)
    {
        if (await repository.CountAttachmentsAsync(ticket.Id, ct) >= SupportTicketRules.MaxAttachmentsPerTicket)
        {
            return Result<SupportTicketAttachment>.Failure(
                $"A ticket can have at most {SupportTicketRules.MaxAttachmentsPerTicket} attachments.", 409);
        }

        var stored = await attachments.StoreAsync(ticket, upload, uploader, isInternal, ct);
        if (!stored.IsSuccess) return stored;

        var attachment = stored.Value!;
        try
        {
            await repository.AddAttachmentAsync(attachment, ct);
            ticket.UpdatedAt = clock.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await attachments.DeleteQuietlyAsync(attachment.ObjectKey, CancellationToken.None);
            throw;
        }

        return stored;
    }
}
