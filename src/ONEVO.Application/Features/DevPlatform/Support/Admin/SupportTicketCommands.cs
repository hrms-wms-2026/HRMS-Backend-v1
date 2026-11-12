using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Admin;

/// <summary>Platform staff open a ticket on a tenant's behalf. Every ticket belongs to exactly one tenant.</summary>
public sealed record CreateSupportTicketCommand(
    Guid? TenantId,
    string? Subject,
    string? Description,
    string? Priority,
    string? Category,
    Guid ActorPlatformUserId) : IRequest<Result<SupportTicketDto>>;

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

public sealed record UpdateSupportTicketStatusCommand(Guid TicketId, string? Status) : IRequest<Result<SupportTicketDto>>;

public sealed class UpdateSupportTicketStatusCommandHandler
    : IRequestHandler<UpdateSupportTicketStatusCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateSupportTicketStatusCommandHandler(ISupportTicketRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SupportTicketDto>.Success(await SupportTicketLabels.ToDtoAsync(_repository, ticket, cancellationToken));
    }
}

/// <summary>Assigns a ticket to an active platform user, or clears the assignee when null.</summary>
public sealed record AssignSupportTicketCommand(Guid TicketId, Guid? AssignedToPlatformUserId) : IRequest<Result<SupportTicketDto>>;

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
    : IRequest<Result<SupportTicketCommentDto>>;

public sealed class AddSupportTicketCommentCommandHandler
    : IRequestHandler<AddSupportTicketCommentCommand, Result<SupportTicketCommentDto>>
{
    private readonly ISupportTicketRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AddSupportTicketCommentCommandHandler(ISupportTicketRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupportTicketCommentDto>.Success(SupportMapper.ToDto(comment));
    }
}

public sealed record UploadSupportTicketAttachmentCommand(
    Guid TicketId,
    SupportUpload Upload,
    bool IsInternal,
    Guid ActorPlatformUserId) : IRequest<Result<SupportTicketAttachmentDto>>;

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
