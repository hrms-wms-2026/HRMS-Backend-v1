using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.DeleteConversationMessage;

/// <summary>The author deletes their own text message. The chat shows "Message deleted"; the full body is kept in the
/// revisions (investigation evidence) and the attachments stop being downloadable.</summary>
public sealed record DeleteConversationMessageCommand(Guid ConversationId, Guid MessageId, bool ForEveryone = true) : IRequest<Result>;

public sealed class DeleteConversationMessageCommandHandler : IRequestHandler<DeleteConversationMessageCommand, Result>
{
    public const string NotAuthor = "Only the author can delete this message.";
    public const string AlreadyRead = "This message has already been seen. You can only delete it for yourself.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<DeleteConversationMessageCommandHandler> _logger;

    public DeleteConversationMessageCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IUnitOfWork unitOfWork, IInboxRealtimePublisher realtime, ILogger<DeleteConversationMessageCommandHandler> logger)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteConversationMessageCommand command, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result.Forbidden(ConversationRules.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result.NotFound(ConversationRules.NotFoundMessage);

        var message = await _conversations.GetMessageAsync(caller.TenantId, command.MessageId, ct);
        if (message is null || message.ConversationId != context.Conversation.Id)
            return Result.NotFound("Message not found.");
        if (message.Kind != ConversationMessageKinds.Text)
            return Result.Forbidden(NotAuthor);
        if (!command.ForEveryone)
        {
            await _conversations.HideMessageForEmployeeAsync(new ConversationMessageHide
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = message.Id, EmployeeId = caller.EmployeeId,
                CreatedById = caller.UserId, CreatedAt = DateTimeOffset.UtcNow
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await ConversationRules.PublishSafelyAsync(
                () => _realtime.MessageHiddenAsync(caller.TenantId, [caller.EmployeeId], context.Conversation.Id, message.Id, ct),
                _logger, "messageHidden");
            return Result.Success();
        }
        if (message.AuthorEmployeeId != caller.EmployeeId)
            return Result.Forbidden(NotAuthor);
        if (message.IsRetracted)
            return Result.Success();
        if (ConversationRules.HasBeenReadByAnother(context, message))
            return Result.Conflict(AlreadyRead);

        var now = DateTimeOffset.UtcNow;
        await _conversations.AddRevisionAsync(new ConversationMessageRevision
        {
            Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = message.Id, BodyHtml = message.BodyHtml, RevisedAt = now,
            RevisedByEmployeeId = caller.EmployeeId, Kind = ConversationRevisionKinds.Delete, CreatedById = caller.UserId, CreatedAt = now
        }, ct);
        message.DeletedAt = now;
        message.DeletedByEmployeeId = caller.EmployeeId;
        _conversations.UpdateMessage(message);
        await _unitOfWork.SaveChangesAsync(ct);

        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageDeletedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, message.Id, ct),
            _logger, "messageDeleted");
        return Result.Success();
    }
}
