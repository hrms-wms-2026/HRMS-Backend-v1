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

namespace ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;

public sealed class PostConversationMessageCommandHandler
    : IRequestHandler<PostConversationMessageCommand, Result<ConversationMessageResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConversationNotifier _notifier;
    private readonly ConversationResponseBuilder _responses;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<PostConversationMessageCommandHandler> _logger;

    public PostConversationMessageCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, IUnitOfWork unitOfWork, ConversationNotifier notifier,
        ConversationResponseBuilder responses, IInboxRealtimePublisher realtime,
        ILogger<PostConversationMessageCommandHandler> logger)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _responses = responses;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<ConversationMessageResponse>> Handle(PostConversationMessageCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ConversationMessageResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<ConversationMessageResponse>.Forbidden(ConversationRules.NoEmployeeMessage);

        var context = await _access.LoadAsync(tenantId, command.ConversationId, caller.Value, ct);
        if (context is null)
            return Result<ConversationMessageResponse>.NotFound(ConversationRules.NotFoundMessage);

        var (body, error) = ConversationRules.ValidateBody(command.BodyHtml);
        if (error is not null)
            return Result<ConversationMessageResponse>.Failure(error);

        var mentionIds = (command.MentionEmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (mentionIds.Any(id => !context.ParticipantIds.Contains(id)))
            return Result<ConversationMessageResponse>.UnprocessableEntity("You can only mention people in this conversation.");

        var now = DateTimeOffset.UtcNow;
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = context.Conversation.Id, AuthorEmployeeId = caller,
            Kind = ConversationMessageKinds.Text, BodyHtml = body!, CreatedById = _currentUser.UserId, CreatedAt = now
        };
        var mentions = mentionIds.Select(id => new ConversationMessageMention
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MessageId = message.Id, EmployeeId = id,
            CreatedById = _currentUser.UserId, CreatedAt = now
        }).ToList();
        var author = (await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, [caller.Value], ct)).GetValueOrDefault(caller.Value);

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _conversations.AddMessageAsync(message, mentions, innerCt);
            context.Conversation.LastMessageAt = now;
            _conversations.Update(context.Conversation);
            if (context.RowOf(caller.Value) is { } ownRow)
            {
                ownRow.LastReadAt = now;
                _conversations.UpdateParticipant(ownRow);
            }
            await _notifier.NotifyAsync(
                tenantId, ConversationNotifier.MentionedTemplate, mentionIds.Where(id => id != caller.Value),
                author?.Name ?? "A teammate", context.SubjectInfo.Title, context.Subject, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        var response = ConversationResponseBuilder.Message(message, mentionIds, author);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageCreatedAsync(tenantId, context.ParticipantIds, context.Conversation.Id, response, ct),
            _logger, "messageCreated");
        return Result<ConversationMessageResponse>.Success(response);
    }
}
