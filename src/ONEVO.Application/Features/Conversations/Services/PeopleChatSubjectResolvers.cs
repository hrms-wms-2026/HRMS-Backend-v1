using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>A person-to-person chat: its two people are its base members (their rows are written when the chat is
/// created), so only they get in and nobody can be added - adding people starts a group instead.</summary>
public sealed class DirectConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IConversationRepository _conversations;

    public DirectConversationSubjectResolver(IConversationRepository conversations) => _conversations = conversations;

    public string Source => ConversationSources.Direct;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        var conversation = await _conversations.GetBySubjectAsync(tenantId, subject, ct);
        if (conversation is null)
            return null;
        var rows = await _conversations.ListParticipantsAsync(tenantId, conversation.Id, ct);
        var people = rows.Where(r => r.JoinReason == ConversationJoinReasons.Base).Select(r => r.EmployeeId).Distinct().ToList();
        return new ConversationSubject(people, PeopleChatNames.Direct, null);
    }
}

/// <summary>A named group: everyone is an added member (trusted from their own active row), so anyone can add
/// people and anyone can leave. There are no base members.</summary>
public sealed class GroupConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IConversationRepository _conversations;

    public GroupConversationSubjectResolver(IConversationRepository conversations) => _conversations = conversations;

    public string Source => ConversationSources.Group;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        var conversation = await _conversations.GetBySubjectAsync(tenantId, subject, ct);
        if (conversation is null)
            return null;
        var names = await _conversations.LatestSystemEventsAsync(tenantId, [conversation.Id], PeopleChatEvents.GroupNameEvents, ct);
        return new ConversationSubject([], PeopleChatNames.GroupName(names.GetValueOrDefault(conversation.Id)), null);
    }
}

/// <summary>System event types of people chats. JSON is always written with "type" first (the repository matches
/// on that prefix).</summary>
public static class PeopleChatEvents
{
    public const string GroupCreated = "group_created";
    public const string GroupRenamed = "group_renamed";
    /// <summary>An inbox item's own conversation became its dedicated group (people added, escalated, next approver).</summary>
    public const string CaseGroup = "case_group";
    /// <summary>A case in a 1-on-1 chat moved to its dedicated group; {"conversationId"} is the group.</summary>
    public const string CaseMoved = "case_moved";
    /// <summary>A case was decided or closed: the "Case resolved · date" line under its card.</summary>
    public const string CaseResolved = "case_resolved";
    /// <summary>A later approver of a multi-step request joined the case group.</summary>
    public const string NextApprover = "next_approver";

    public static readonly IReadOnlyList<string> GroupNameEvents = [GroupCreated, GroupRenamed];
}

public static class PeopleChatNames
{
    public const string Direct = "Direct message";
    public const string UnnamedGroup = "Group";
    public const int MaxGroupName = 80;

    /// <summary>The group's name from its latest group_created / group_renamed message.</summary>
    public static string GroupName(ConversationMessage? nameEvent)
    {
        if (nameEvent?.SystemEventJson is not { } json)
            return UnnamedGroup;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } value
                ? value
                : UnnamedGroup;
        }
        catch (System.Text.Json.JsonException)
        {
            return UnnamedGroup;
        }
    }

    /// <summary>The one chat of a pair: the same id whichever of the two asks.</summary>
    public static Guid DirectSubjectId(Guid a, Guid b)
    {
        var (low, high) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        return DeterministicId($"direct:{low:N}:{high:N}");
    }

    /// <summary>A stable id for a message that must exist once (a case's card in a chat, its resolved line, ...), so
    /// concurrent list loads agree and inserting it again is a no-op.</summary>
    public static Guid DeterministicId(string key)
        => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
}
