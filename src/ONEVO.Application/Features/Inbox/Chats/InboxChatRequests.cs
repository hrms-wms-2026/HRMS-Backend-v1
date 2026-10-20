using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>Where a chat is opened: POST /conversations/open with this subject.</summary>
public sealed record InboxChatRef(Guid ConversationId, string Source, string Kind, Guid SubjectId);

public sealed record ListInboxChatsQuery : IRequest<Result<IReadOnlyList<InboxChatRow>>>;
public sealed record GetInboxChatForItemQuery(string Source, string Kind, Guid Id) : IRequest<Result<InboxChatRow>>;
public sealed record OpenDirectChatCommand(Guid EmployeeId) : IRequest<Result<InboxChatRef>>;
public sealed record CreateGroupChatCommand(string Name, IReadOnlyList<Guid> EmployeeIds) : IRequest<Result<InboxChatRef>>;
public sealed record RenameGroupChatCommand(Guid ConversationId, string Name) : IRequest<Result>;
/// <summary>Moves a case from the 1-on-1 chat to its own group and adds people to it.</summary>
public sealed record StartCaseGroupCommand(string Source, string Kind, Guid Id, IReadOnlyList<Guid> EmployeeIds) : IRequest<Result<InboxChatRef>>;

public sealed class InboxChatHandlers :
    IRequestHandler<ListInboxChatsQuery, Result<IReadOnlyList<InboxChatRow>>>,
    IRequestHandler<GetInboxChatForItemQuery, Result<InboxChatRow>>,
    IRequestHandler<OpenDirectChatCommand, Result<InboxChatRef>>,
    IRequestHandler<CreateGroupChatCommand, Result<InboxChatRef>>,
    IRequestHandler<RenameGroupChatCommand, Result>,
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
    private readonly ILogger<InboxChatHandlers> _logger;

    public InboxChatHandlers(
        InboxCallerResolver callers, IInboxChatService chats, IPeopleChatService people, ICaseGroupService caseGroups,
        IConversationAccess access, IConversationRepository conversations, ICallerIdentityResolver identity,
        IMilestoneMembershipCoordinator membership, IInboxRealtimePublisher realtime, ISender sender, ILogger<InboxChatHandlers> logger)
    {
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
        if (context is null || context.Conversation.SubjectSource != ConversationSources.Group)
            return Result.NotFound(NotFoundMessage);
        var line = await _people.RenameGroupAsync(caller.TenantId, context.Conversation, caller.EmployeeId, await CallerNameAsync(caller, ct), name, ct);
        var message = ConversationResponseBuilder.Message(line, [], author: null);
        await ConversationRules.PublishSafelyAsync(
            () => _realtime.MessageCreatedAsync(caller.TenantId, context.ParticipantIds, context.Conversation.Id, message, ct), _logger, "messageCreated");
        return Result.Success();
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

        var group = await _caseGroups.EnsureAsync(caller.TenantId, item, caller.EmployeeId, ct);
        if (group is null)
            return Result<InboxChatRef>.NotFound(NotFoundMessage);
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

    private async Task<string> CallerNameAsync(InboxCaller caller, CancellationToken ct)
        => (await _identity.ResolveIdentitiesByEmployeeIdAsync(caller.TenantId, [caller.EmployeeId], ct))
            .GetValueOrDefault(caller.EmployeeId)?.Name ?? "Someone";

    private static InboxChatRef Ref(Conversation chat) => new(chat.Id, chat.SubjectSource, chat.SubjectKind, chat.SubjectId);
}
