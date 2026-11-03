using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage;

/// <summary>A conversation member sets their reaction on a message (<see cref="Emoji"/>) or removes it (null). Each
/// person has one reaction per message: a new emoji replaces their earlier one. Not on system or deleted messages.</summary>
public sealed record ReactToConversationMessageCommand(Guid ConversationId, Guid MessageId, string? Emoji)
    : IRequest<Result<ConversationMessageResponse>>;

public sealed class ReactToConversationMessageCommandHandler
    : IRequestHandler<ReactToConversationMessageCommand, Result<ConversationMessageResponse>>
{
    public const string MessageNotFound = "Message not found.";
    public const string NotReactable = "You can only react to messages people wrote.";
    public const string InvalidEmoji = "Pick one emoji to react with.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConversationResponseBuilder _responses;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<ReactToConversationMessageCommandHandler> _logger;
    private readonly IEnumerable<IConversationActivityListener>? _listeners;

    public ReactToConversationMessageCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IUnitOfWork unitOfWork, ConversationResponseBuilder responses, IInboxRealtimePublisher realtime,
        ILogger<ReactToConversationMessageCommandHandler> logger, IEnumerable<IConversationActivityListener>? listeners = null)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _responses = responses;
        _realtime = realtime;
        _logger = logger;
        _listeners = listeners;
    }

    public async Task<Result<ConversationMessageResponse>> Handle(ReactToConversationMessageCommand command, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result<ConversationMessageResponse>.Forbidden(ConversationRules.NoEmployeeMessage);
        var emoji = command.Emoji?.Trim();
        if (emoji is not null && (emoji.Length == 0 || emoji.Length > ConversationRules.MaxEmojiLength
            || emoji.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '&')))
            return Result<ConversationMessageResponse>.UnprocessableEntity(InvalidEmoji);

        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result<ConversationMessageResponse>.NotFound(ConversationRules.NotFoundMessage);
        var message = await _conversations.GetMessageAsync(caller.TenantId, command.MessageId, ct);
        if (message is null || message.ConversationId != context.Conversation.Id)
            return Result<ConversationMessageResponse>.NotFound(MessageNotFound);
        if (message.Kind != ConversationMessageKinds.Text || message.IsRetracted)
            return Result<ConversationMessageResponse>.UnprocessableEntity(NotReactable);

        var existing = await _conversations.GetReactionAsync(caller.TenantId, message.Id, caller.EmployeeId, ct);
        if (emoji is null)
        {
            if (existing is not null)
                _conversations.RemoveReaction(existing);
        }
        else if (existing is null)
        {
            await _conversations.AddReactionAsync(new ConversationMessageReaction
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = message.Id, EmployeeId = caller.EmployeeId,
                Emoji = emoji, CreatedById = caller.UserId, CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }
        else if (existing.Emoji != emoji)
        {
            // Replacing counts as a new reaction: it moves to the end of the order (reacted-at = UpdatedAt).
            existing.Emoji = emoji;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _unitOfWork.SaveChangesAsync(ct);

        var response = (await _responses.MessagesAsync(caller.TenantId, [message], ct))[0];
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageUpdatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, response, ct),
            _logger, "messageUpdated");
        await ConversationRules.NotifyActivityAsync(_listeners, caller.TenantId, context.Subject, caller.EmployeeId,
            ConversationActivities.Reacted, _logger, ct);
        return Result<ConversationMessageResponse>.Success(response);
    }
}
