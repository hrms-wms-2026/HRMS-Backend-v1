using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>Where a chat is opened: POST /conversations/open with this subject.</summary>
public sealed record InboxChatRef(Guid ConversationId, string Source, string Kind, Guid SubjectId);

public sealed record ListInboxChatsQuery : IRequest<Result<IReadOnlyList<InboxChatRow>>>;
public sealed record GetInboxChatForItemQuery(string Source, string Kind, Guid Id) : IRequest<Result<InboxChatRow>>;
public sealed record OpenDirectChatCommand(Guid EmployeeId) : IRequest<Result<InboxChatRef>>;
/// <summary><see cref="AvatarFileId"/>: an image the caller uploaded (purpose conversation_avatar) to show as the group's photo.</summary>
public sealed record CreateGroupChatCommand(string Name, IReadOnlyList<Guid> EmployeeIds, Guid? AvatarFileId = null) : IRequest<Result<InboxChatRef>>;
public sealed record RenameGroupChatCommand(Guid ConversationId, string Name) : IRequest<Result>;
/// <summary>Pins a chat to the top of its section in the caller's own inbox (or unpins it).</summary>
public sealed record SetChatFavoriteCommand(Guid ConversationId, bool Favorite) : IRequest<Result>;
/// <summary>Sends a copy of a case card into chats the caller is in (WhatsApp-style forward). Everyone there sees it; only
/// the people the case's module lets act get its actions, since each viewer's card loads their own view of it.</summary>
public sealed record ForwardCaseCommand(string Source, string Kind, Guid Id, IReadOnlyList<Guid> ConversationIds) : IRequest<Result<int>>;
/// <summary>Sends a copy of a chat message (its text and files) into chats the caller is in, labelled "forwarded".</summary>
public sealed record ForwardMessageCommand(Guid MessageId, IReadOnlyList<Guid> ConversationIds) : IRequest<Result<int>>;
/// <summary>Moves a case from the 1-on-1 chat to its own group and adds people to it.</summary>
/// <summary><see cref="Name"/>: the group's name when it should differ from the case title; <see cref="AvatarFileId"/>: its photo.</summary>
public sealed record StartCaseGroupCommand(
    string Source, string Kind, Guid Id, IReadOnlyList<Guid> EmployeeIds, string? Name = null, Guid? AvatarFileId = null) : IRequest<Result<InboxChatRef>>;

public sealed class InboxChatHandlers :
    IRequestHandler<ListInboxChatsQuery, Result<IReadOnlyList<InboxChatRow>>>,
    IRequestHandler<GetInboxChatForItemQuery, Result<InboxChatRow>>,
    IRequestHandler<OpenDirectChatCommand, Result<InboxChatRef>>,
    IRequestHandler<CreateGroupChatCommand, Result<InboxChatRef>>,
    IRequestHandler<RenameGroupChatCommand, Result>,
    IRequestHandler<SetChatFavoriteCommand, Result>,
    IRequestHandler<ForwardCaseCommand, Result<int>>,
    IRequestHandler<ForwardMessageCommand, Result<int>>,
    IRequestHandler<StartCaseGroupCommand, Result<InboxChatRef>>
{
    public const string NotFoundMessage = "Chat not found.";
    public const string InactiveMessage = "Only active employees of this organization can be added.";
    public const string NameRequiredMessage = "Give the group a name.";

    private readonly InboxCallerResolver _callers;
    private readonly IInboxChatService _chats;
    private readonly IPeopleChatService _people;
    private readonly ICaseGroupService _caseGroups;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IInboxRealtimePublisher _realtime;
    private readonly ISender _sender;
    private readonly IInboxRouter _router;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IPrimaryEntityAssetLinker _assets;
    private readonly ILogger<InboxChatHandlers> _logger;

    public InboxChatHandlers(
        InboxCallerResolver callers, IInboxChatService chats, IPeopleChatService people, ICaseGroupService caseGroups,
        IConversationAccess access, IConversationRepository conversations, ICallerIdentityResolver identity,
        IMilestoneMembershipCoordinator membership, IInboxRealtimePublisher realtime, ISender sender, ILogger<InboxChatHandlers> logger,
        IInboxRouter router, IUnitOfWork unitOfWork, ICurrentUser currentUser, IPrimaryEntityAssetLinker assets)
    {
        _currentUser = currentUser;
        _assets = assets;
        _unitOfWork = unitOfWork;
        _callers = callers;
        _chats = chats;
        _people = people;
        _caseGroups = caseGroups;
        _access = access;
        _conversations = conversations;
        _identity = identity;
        _membership = membership;
        _realtime = realtime;
        _sender = sender;
        _router = router;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<InboxChatRow>>> Handle(ListInboxChatsQuery query, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<IReadOnlyList<InboxChatRow>>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        return Result<IReadOnlyList<InboxChatRow>>.Success(await _chats.ListAsync(caller.TenantId, caller.EmployeeId, ct));
    }

    public async Task<Result<InboxChatRow>> Handle(GetInboxChatForItemQuery query, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<InboxChatRow>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var row = await _chats.ForItemAsync(caller.TenantId, caller.EmployeeId, new ItemRef(query.Source, query.Kind, query.Id), ct);
        return row is null ? Result<InboxChatRow>.NotFound(NotFoundMessage) : Result<InboxChatRow>.Success(row);
    }

    public async Task<Result<InboxChatRef>> Handle(OpenDirectChatCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<InboxChatRef>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        if (command.EmployeeId == caller.EmployeeId || command.EmployeeId == Guid.Empty)
            return Result<InboxChatRef>.UnprocessableEntity("Pick someone else to chat with.");
        if (await _membership.GetActiveAssigneeAsync(caller.TenantId, command.EmployeeId, ct) is null)
            return Result<InboxChatRef>.UnprocessableEntity(InactiveMessage);
        var chat = await _people.GetOrCreateDirectAsync(caller.TenantId, caller.EmployeeId, command.EmployeeId, ct);
        return Result<InboxChatRef>.Success(Ref(chat));
    }

    public async Task<Result<InboxChatRef>> Handle(CreateGroupChatCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<InboxChatRef>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return Result<InboxChatRef>.UnprocessableEntity(NameRequiredMessage);
        if (name.Length > PeopleChatNames.MaxGroupName)
            return Result<InboxChatRef>.UnprocessableEntity($"Keep the name under {PeopleChatNames.MaxGroupName} characters.");
        var members = (command.EmployeeIds ?? []).Where(id => id != Guid.Empty && id != caller.EmployeeId).Distinct().ToList();
        if (members.Count == 0)
            return Result<InboxChatRef>.UnprocessableEntity("Add at least one person.");
        foreach (var employeeId in members)
            if (await _membership.GetActiveAssigneeAsync(caller.TenantId, employeeId, ct) is null)
                return Result<InboxChatRef>.UnprocessableEntity(InactiveMessage);

        var creator = await CallerNameAsync(caller, ct);
        var group = await _people.CreateGroupAsync(caller.TenantId, caller.EmployeeId, creator, name, members, ct);
        await LinkAvatarAsync(caller, group.Id, command.AvatarFileId, ct);
        var subject = new ItemRef(group.SubjectSource, group.SubjectKind, group.SubjectId);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.InboxChangedAsync(caller.TenantId, members.Append(caller.EmployeeId).ToList(), subject, ct), _logger, "inboxChanged");
        return Result<InboxChatRef>.Success(Ref(group));
    }

    public async Task<Result> Handle(RenameGroupChatCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return Result.UnprocessableEntity(NameRequiredMessage);
        if (name.Length > PeopleChatNames.MaxGroupName)
            return Result.UnprocessableEntity($"Keep the name under {PeopleChatNames.MaxGroupName} characters.");
        var context = await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct);
        // A named group, or a case group (once started: it has its marker). A 1-on-1 chat has no name to change.
        if (context is null || context.Conversation.SubjectSource == ConversationSources.Direct
            || (context.Conversation.SubjectSource != ConversationSources.Group
                && await _caseGroups.FindAsync(caller.TenantId, context.Subject, ct) is null))
            return Result.NotFound(NotFoundMessage);
        var line = await _people.RenameGroupAsync(caller.TenantId, context.Conversation, caller.EmployeeId, await CallerNameAsync(caller, ct), name, ct);
        var message = ConversationResponseBuilder.Message(line, [], author: null);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageCreatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, message, ct), _logger, "messageCreated");
        return Result.Success();
    }

    public async Task<Result> Handle(SetChatFavoriteCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        // Only a chat the caller is in can be pinned.
        if (await _access.LoadAsync(caller.TenantId, command.ConversationId, caller.EmployeeId, ct) is null)
            return Result.NotFound(NotFoundMessage);
        await _conversations.SetFavoriteAsync(caller.TenantId, caller.EmployeeId, command.ConversationId, command.Favorite, ct);
        return Result.Success();
    }

    public async Task<Result<int>> Handle(ForwardCaseCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<int>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var targets = (command.ConversationIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (targets.Count == 0)
            return Result<int>.UnprocessableEntity("Pick at least one chat.");
        // Only someone who can see the case may pass it on.
        var detail = await _router.GetDetailAsync(caller.TenantId, caller.EmployeeId, command.Source, command.Kind, command.Id, ct);
        if (!detail.IsSuccess)
            return Result<int>.NotFound(NotFoundMessage);
        var item = new ItemRef(command.Source, command.Kind, command.Id);
        var title = detail.Value!.Item.Title;
        var author = (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct)).GetValueOrDefault(caller.EmployeeId);

        var contexts = new List<ConversationContext>();
        foreach (var conversationId in targets)
        {
            // Into chats the caller is in only.
            var context = await _access.LoadAsync(caller.TenantId, conversationId, caller.EmployeeId, ct);
            if (context is null)
                return Result<int>.NotFound(NotFoundMessage);
            contexts.Add(context);
        }

        foreach (var context in contexts)
        {
            var now = DateTimeOffset.UtcNow;
            var card = new ConversationMessage
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, ConversationId = context.Conversation.Id, AuthorEmployeeId = caller.EmployeeId,
                Kind = ConversationMessageKinds.Case, BodyHtml = System.Net.WebUtility.HtmlEncode(title),
                SystemEventJson = CaseCards.CardJson(item, title, forwarded: true), CreatedAt = now,
            };
            await _conversations.AddMessageIfMissingAsync(card, ct);
            await _conversations.TouchAsync(caller.TenantId, context.Conversation.Id, now, ct);
            var message = ConversationResponseBuilder.Message(card, [], author);
            await ConversationRules.PublishSafelyAsync(
                () => _realtime.MessageCreatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, message, ct), _logger, "messageCreated");
        }
        return Result<int>.Success(contexts.Count);
    }

    public async Task<Result<int>> Handle(ForwardMessageCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<int>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var targets = (command.ConversationIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (targets.Count == 0)
            return Result<int>.UnprocessableEntity("Pick at least one chat.");
        // Only a live text message from a 1-on-1 chat or named group the caller is in. A case's own conversation (an
        // investigation, a request thread) is limited to the people the case's module allows, so its messages stay there.
        var source = await _conversations.GetMessageAsync(caller.TenantId, command.MessageId, ct);
        if (source is null || source.IsRetracted || source.Kind != ConversationMessageKinds.Text)
            return Result<int>.NotFound("Message not found.");
        var sourceContext = await _access.LoadAsync(caller.TenantId, source.ConversationId, caller.EmployeeId, ct);
        if (sourceContext is null)
            return Result<int>.NotFound("Message not found.");
        if (!ConversationSources.IsPeopleChat(sourceContext.Conversation.SubjectSource))
            return Result<int>.UnprocessableEntity("Messages in a case's group can't be forwarded.");

        var contexts = new List<ConversationContext>();
        foreach (var conversationId in targets)
        {
            var context = await _access.LoadAsync(caller.TenantId, conversationId, caller.EmployeeId, ct);
            if (context is null)
                return Result<int>.NotFound(NotFoundMessage);
            contexts.Add(context);
        }

        var files = (await _conversations.ListAttachmentsAsync(caller.TenantId, [source.Id], ct)).GetValueOrDefault(source.Id) ?? [];
        var author = (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct)).GetValueOrDefault(caller.EmployeeId);
        foreach (var context in contexts)
        {
            var now = DateTimeOffset.UtcNow;
            // A copy, not a link: it reads the same wherever it went, and its files open through the new chat's access.
            var copy = new ConversationMessage
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, ConversationId = context.Conversation.Id, AuthorEmployeeId = caller.EmployeeId,
                Kind = ConversationMessageKinds.Text, BodyHtml = source.BodyHtml, Tag = ConversationMessageTags.Forwarded, CreatedAt = now,
            };
            var attachments = files.Select(f => new ConversationMessageAttachment
            {
                Id = Guid.NewGuid(), TenantId = caller.TenantId, MessageId = copy.Id, FileRecordId = f.FileRecordId,
                FileName = f.FileName, ContentType = f.ContentType, SizeBytes = f.SizeBytes, CreatedAt = now,
            }).ToList();
            await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _conversations.AddMessageAsync(copy, [], innerCt);
                await _conversations.AddAttachmentsAsync(attachments, innerCt);
                context.Conversation.LastMessageAt = now;
                _conversations.Update(context.Conversation);
                await _unitOfWork.SaveChangesAsync(innerCt);
                return true;
            }, ct);
            var message = ConversationResponseBuilder.Message(copy, [], author, attachments);
            await ConversationRules.PublishSafelyAsync(
                () => _realtime.MessageCreatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, message, ct), _logger, "messageCreated");
        }
        return Result<int>.Success(contexts.Count);
    }

    public async Task<Result<InboxChatRef>> Handle(StartCaseGroupCommand command, CancellationToken ct)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller)
            return Result<InboxChatRef>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
        var item = new ItemRef(command.Source, command.Kind, command.Id);
        var subject = await _access.ResolveSubjectAsync(caller.TenantId, item, ct);
        // Only someone in the case (requester, reviewer, someone who can see it) may start its group.
        if (subject is null || (!subject.BaseParticipantEmployeeIds.Contains(caller.EmployeeId)
                && !await _access.CanViewAsync(caller.TenantId, item, caller.EmployeeId, ct)))
            return Result<InboxChatRef>.NotFound(NotFoundMessage);

        var name = command.Name?.Trim();
        if (name is { Length: > PeopleChatNames.MaxGroupName })
            return Result<InboxChatRef>.UnprocessableEntity($"Keep the name under {PeopleChatNames.MaxGroupName} characters.");

        var group = await _caseGroups.EnsureAsync(caller.TenantId, item, caller.EmployeeId, ct);
        if (group is null)
            return Result<InboxChatRef>.NotFound(NotFoundMessage);
        // A name of its own only when it differs from what the group is called now (the case title, or an earlier name).
        if (!string.IsNullOrEmpty(name))
        {
            var current = (await _conversations.LatestSystemEventsAsync(caller.TenantId, [group.Id], PeopleChatEvents.GroupNameEvents, ct))
                .GetValueOrDefault(group.Id);
            var currentName = current is null ? subject.Title : PeopleChatNames.GroupName(current);
            if (!string.Equals(name, currentName, StringComparison.Ordinal))
                await _people.RenameGroupAsync(caller.TenantId, group, caller.EmployeeId, await CallerNameAsync(caller, ct), name, ct);
        }
        await LinkAvatarAsync(caller, group.Id, command.AvatarFileId, ct);
        var toAdd = (command.EmployeeIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (toAdd.Count > 0)
        {
            var added = await _sender.Send(new AddConversationParticipantsCommand(group.Id, toAdd), ct);
            if (!added.IsSuccess)
                return Result<InboxChatRef>.Failure(added.Error ?? "People could not be added.", added.StatusCode ?? 400);
        }
        var members = await _conversations.ListActiveMemberIdsAsync(caller.TenantId, [group.Id], ct);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.InboxChangedAsync(caller.TenantId, members.GetValueOrDefault(group.Id) ?? [], item, ct), _logger, "inboxChanged");
        return Result<InboxChatRef>.Success(Ref(group));
    }

    /// <summary>Makes an uploaded image the group's photo. The group already exists by now, so a file that cannot be
    /// linked (gone, too big, not an image) leaves it without a photo rather than failing the whole request.</summary>
    private async Task LinkAvatarAsync(InboxCaller caller, Guid conversationId, Guid? fileId, CancellationToken ct)
    {
        if (fileId is not { } id || id == Guid.Empty)
            return;
        var linked = await _assets.LinkAsync(caller.TenantId, _currentUser.UserId, EntityAssetOwnerTypes.Conversation, conversationId,
            UploadPurposeCatalog.ConversationAvatar, id, ct);
        if (!linked.IsSuccess)
            _logger.LogWarning("Group photo {FileId} could not be linked to conversation {ConversationId}: {Error}", id, conversationId, linked.Error);
    }

    private async Task<string> CallerNameAsync(InboxCaller caller, CancellationToken ct)
        => (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct))
            .GetValueOrDefault(caller.EmployeeId)?.Name ?? "Someone";

    private static InboxChatRef Ref(Conversation chat) => new(chat.Id, chat.SubjectSource, chat.SubjectKind, chat.SubjectId);
}
