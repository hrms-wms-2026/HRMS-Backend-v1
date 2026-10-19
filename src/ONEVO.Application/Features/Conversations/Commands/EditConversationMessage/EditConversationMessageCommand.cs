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

namespace ONEVO.Application.Features.Conversations.Commands.EditConversationMessage;

/// <summary>The author rewrites their own text message (any time, not once deleted). The previous body is kept as
/// a revision; only people mentioned for the first time are notified.</summary>
public sealed record EditConversationMessageCommand(
    Guid ConversationId, Guid MessageId, string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds)
    : IRequest<Result<ConversationMessageResponse>>;

public sealed class EditConversationMessageCommandHandler
    : IRequestHandler<EditConversationMessageCommand, Result<ConversationMessageResponse>>
{
    public const string MessageNotFound = "Message not found.";
    public const string NotAuthor = "Only the author can edit this message.";
    public const string NotEditable = "Only text messages can be edited.";
    public const string AlreadyDeleted = "This message was deleted.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConversationNotifier _notifier;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<EditConversationMessageCommandHandler> _logger;
    private readonly ConversationResponseBuilder? _responses;

    public EditConversationMessageCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IUnitOfWork unitOfWork, ConversationNotifier notifier, IInboxRealtimePublisher realtime,
        ILogger<EditConversationMessageCommandHandler> logger, ConversationResponseBuilder? responses = null)
    {
        _responses = responses;
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<Result<ConversationMessageResponse>> Handle(EditConversationMessageCommand command, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result<ConversationMessageResponse>.Forbidden(ConversationRules.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result<ConversationMessageResponse>.NotFound(ConversationRules.NotFoundMessage);

        var message = await _conversations.GetMessageAsync(caller.TenantId, command.MessageId, ct);
        if (message is null || message.ConversationId != context.Conversation.Id)
            return Result<ConversationMessageResponse>.NotFound(MessageNotFound);
        if (message.AuthorEmployeeId != caller.EmployeeId)
            return Result<ConversationMessageResponse>.Forbidden(NotAuthor);
        if (message.Kind != ConversationMessageKinds.Text)
            return Result<ConversationMessageResponse>.UnprocessableEntity(NotEditable);
        if (message.IsRetracted)
            return Result<ConversationMessageResponse>.Conflict(AlreadyDeleted);

        var attachments = (await _conversations.ListAttachmentsAsync(caller.TenantId, [message.Id], ct)).GetValueOrDefault(message.Id);
        var (body, error) = ConversationRules.ValidateBody(command.BodyHtml, allowEmpty: attachments is { Count: > 0 });
        if (error is not null)
            return Result<ConversationMessageResponse>.Failure(error);
        var mentionIds = (command.MentionEmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (mentionIds.Any(id => !context.ParticipantIds.Contains(id)))
            return Result<ConversationMessageResponse>.UnprocessableEntity("You can only mention people in this conversation.");

        var previousMentions = (await _conversations.ListMentionsAsync(caller.TenantId, [message.Id], ct)).GetValueOrDefault(message.Id) ?? [];
        var newMentions = mentionIds.Except(previousMentions).ToList();
        var now = DateTimeOffset.UtcNow;
        var author = (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct)).GetValueOrDefault(caller.EmployeeId);

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _conversations.AddRevisionAsync(new ConversationMessageRevision
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = message.Id, BodyHtml = message.BodyHtml,
                RevisedAt = now, RevisedByEmployeeId = caller.EmployeeId, Kind = ConversationRevisionKinds.Edit,
                CreatedById = caller.UserId, CreatedAt = now
            }, innerCt);
            message.BodyHtml = body!;
            message.EditedAt = now;
            _conversations.UpdateMessage(message);
            if (newMentions.Count > 0)
            {
                await _conversations.AddMentionsAsync(newMentions.Select(id => new ConversationMessageMention
                {
                    Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = message.Id, EmployeeId = id,
                    CreatedById = caller.UserId, CreatedAt = now
                }).ToList(), innerCt);
                await _notifier.NotifyAsync(caller.TenantId, ConversationNotifier.MentionedTemplate,
                    newMentions.Where(id => id != caller.EmployeeId), author?.Name ?? "A teammate", context.SubjectInfo.Title,
                    context.Subject, innerCt);
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        // An edit keeps the quote it was posted with.
        var replyTo = _responses is null ? null : await _responses.ReplyPreviewAsync(caller.TenantId, message, ct);
        var response = ConversationResponseBuilder.Message(message, previousMentions.Union(newMentions).ToList(), author, attachments, replyTo);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageUpdatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, response, ct),
            _logger, "messageUpdated");
        return Result<ConversationMessageResponse>.Success(response);
    }
}
