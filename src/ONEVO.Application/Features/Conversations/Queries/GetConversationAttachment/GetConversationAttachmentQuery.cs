using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.Conversations.Queries.GetConversationAttachment;

public sealed record ConversationAttachmentDownload(Stream Content, string ContentType, string FileName);

/// <summary>Streams one chat attachment to a current participant. Never a public URL: the access check runs on
/// every download, so a removed or departed member is refused at once.</summary>
public sealed record GetConversationAttachmentQuery(Guid ConversationId, Guid AttachmentId) : IRequest<Result<ConversationAttachmentDownload>>;

public sealed class GetConversationAttachmentQueryHandler
    : IRequestHandler<GetConversationAttachmentQuery, Result<ConversationAttachmentDownload>>
{
    public const string AttachmentNotFound = "Attachment not found.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly IFileStorageService _files;

    public GetConversationAttachmentQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access, IConversationRepository conversations,
        IFileStorageService files)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _files = files;
    }

    public async Task<Result<ConversationAttachmentDownload>> Handle(GetConversationAttachmentQuery query, CancellationToken ct)
    {
        if (await ConversationCaller.ResolveAsync(_currentUser, _identity, ct) is not { } caller)
            return Result<ConversationAttachmentDownload>.Forbidden(ConversationRules.NoEmployeeMessage);
        var context = await _access.LoadAsync(caller.TenantId, query.ConversationId, caller.EmployeeId, ct);
        if (context is null)
            return Result<ConversationAttachmentDownload>.NotFound(ConversationRules.NotFoundMessage);

        var attachment = await _conversations.GetAttachmentAsync(caller.TenantId, query.AttachmentId, ct);
        var message = attachment is null ? null : await _conversations.GetMessageAsync(caller.TenantId, attachment.MessageId, ct);
        if (attachment is null || message is null || message.ConversationId != context.Conversation.Id || message.IsRetracted
            || await _conversations.IsMessageHiddenForEmployeeAsync(caller.TenantId, message.Id, caller.EmployeeId, ct))
            return Result<ConversationAttachmentDownload>.NotFound(AttachmentNotFound);

        var stream = await _files.OpenReadAsync(caller.TenantId, attachment.FileRecordId, ct);
        return stream.IsSuccess
            ? Result<ConversationAttachmentDownload>.Success(new ConversationAttachmentDownload(
                stream.Value!.Content, attachment.ContentType, attachment.FileName))
            : Result<ConversationAttachmentDownload>.NotFound(AttachmentNotFound);
    }
}
