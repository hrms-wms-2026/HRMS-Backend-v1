using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;

public sealed class PostConversationMessageCommandHandler
    : IRequestHandler<PostConversationMessageCommand, Result<ConversationMessageResponse>>
{
    public const int MaxAttachments = 5;
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;
    public const string TooManyAttachments = "You can attach at most 5 files to a message.";
    public const string AttachmentTooLarge = "Each attachment can be at most 25 MB.";
    public const string AttachmentNotYours = "Attach files you uploaded for this message.";
    public const string JustificationSubjectOnly = "Only the employee this item is about can post a justification.";
    public const string UnknownTag = "This message label is not allowed.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConversationNotifier _notifier;
    private readonly ConversationResponseBuilder _responses;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ILogger<PostConversationMessageCommandHandler> _logger;
    private readonly IFileStorageService? _files;

    public PostConversationMessageCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, IUnitOfWork unitOfWork, ConversationNotifier notifier,
        ConversationResponseBuilder responses, IInboxRealtimePublisher realtime,
        ILogger<PostConversationMessageCommandHandler> logger, IFileStorageService? files = null)
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
        _files = files;
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

        var tag = string.IsNullOrWhiteSpace(command.Tag) ? null : command.Tag.Trim();
        if (tag is not null && tag != ConversationMessageTags.Justification)
            return Result<ConversationMessageResponse>.UnprocessableEntity(UnknownTag);
        if (tag == ConversationMessageTags.Justification && context.SubjectInfo.SubjectEmployeeId != caller.Value)
            return Result<ConversationMessageResponse>.Forbidden(JustificationSubjectOnly);

        var fileIds = (command.AttachmentFileIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (fileIds.Count > MaxAttachments)
            return Result<ConversationMessageResponse>.UnprocessableEntity(TooManyAttachments);

        var (body, error) = ConversationRules.ValidateBody(command.BodyHtml, allowEmpty: fileIds.Count > 0);
        if (error is not null)
            return Result<ConversationMessageResponse>.Failure(error);

        var mentionIds = (command.MentionEmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (mentionIds.Any(id => !context.ParticipantIds.Contains(id)))
            return Result<ConversationMessageResponse>.UnprocessableEntity("You can only mention people in this conversation.");

        var now = DateTimeOffset.UtcNow;
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = context.Conversation.Id, AuthorEmployeeId = caller,
            Kind = ConversationMessageKinds.Text, BodyHtml = body!, Tag = tag, CreatedById = _currentUser.UserId, CreatedAt = now
        };

        var attachments = new List<ConversationMessageAttachment>();
        foreach (var fileId in fileIds)
        {
            // Untrusted client ids: only a live file the caller uploaded, within the size limit.
            var record = _files is null ? null : await _files.GetRecordAsync(tenantId, fileId, ct);
            if (record is not { IsSuccess: true } || record.Value!.DeletedAt is not null || record.Value.UploadedByUserId != _currentUser.UserId)
                return Result<ConversationMessageResponse>.UnprocessableEntity(AttachmentNotYours);
            if (record.Value.FileSizeBytes > MaxAttachmentBytes)
                return Result<ConversationMessageResponse>.UnprocessableEntity(AttachmentTooLarge);
            attachments.Add(new ConversationMessageAttachment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, MessageId = message.Id, FileRecordId = fileId,
                FileName = record.Value.OriginalFileName, ContentType = record.Value.ContentType, SizeBytes = record.Value.FileSizeBytes,
                CreatedById = _currentUser.UserId, CreatedAt = now
            });
        }

        var mentions = mentionIds.Select(id => new ConversationMessageMention
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MessageId = message.Id, EmployeeId = id,
            CreatedById = _currentUser.UserId, CreatedAt = now
        }).ToList();
        var author = (await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, [caller.Value], ct)).GetValueOrDefault(caller.Value);

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _conversations.AddMessageAsync(message, mentions, innerCt);
            await _conversations.AddAttachmentsAsync(attachments, innerCt);
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

        var response = ConversationResponseBuilder.Message(message, mentionIds, author, attachments);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageCreatedAsync(tenantId, context.ParticipantIds, context.Conversation.Id, response, ct),
            _logger, "messageCreated");
        return Result<ConversationMessageResponse>.Success(response);
    }
}
