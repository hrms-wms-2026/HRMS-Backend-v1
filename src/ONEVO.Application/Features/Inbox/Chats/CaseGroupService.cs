using System.Net;
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>
/// A case normally lives in the 1-on-1 chat between the person it is about and their reviewer. It moves to its own
/// group - the item's own conversation - when someone starts one, when it escalates, or when a later approver takes
/// over. This service makes that group exist exactly once: its marker line, a row for every base person (requester,
/// approvers) and for the people of the 1-on-1 chats that carry its card, the case card at the top, and a
/// "Continued in a group" line in each of those chats. Every write is idempotent (deterministic ids).
/// </summary>
public interface ICaseGroupService
{
    /// <summary>The item's group, created when missing. Null when no module owns the item.</summary>
    Task<Conversation?> EnsureAsync(Guid tenantId, ItemRef item, Guid? actorEmployeeId, CancellationToken ct);

    /// <summary>The item's group when someone already started one (it has its marker line), else null. Never creates it.</summary>
    Task<Conversation?> FindAsync(Guid tenantId, ItemRef item, CancellationToken ct);

    /// <summary>An escalation of a case that has no group: instead of starting one, the case goes to the 1-on-1 chat of the
    /// person it is about and each new reviewer (its card plus an "Escalated to X by Y" line - a system line, never a
    /// message in the requester's name), and the chats that carried it so far get an "Escalated to X" line.</summary>
    Task PlaceEscalationInDirectChatsAsync(
        Guid tenantId, ItemRef item, Guid subjectEmployeeId, IReadOnlyList<Guid> targetEmployeeIds, Guid? actorEmployeeId,
        string? note, CancellationToken ct);

    /// <summary>Adds a later approver to the case group with a "Next approver" line (not "X added Y"). No-op when they
    /// are already a member.</summary>
    Task AddNextApproverAsync(Guid tenantId, Conversation group, Guid employeeId, CancellationToken ct);
}

public sealed class CaseGroupService : ICaseGroupService
{
    private readonly IConversationRepository _conversations;
    private readonly IConversationAccess _access;
    private readonly ICallerIdentityResolver _identity;
    private readonly IDateTimeProvider _clock;
    private readonly IPeopleChatService? _people;

    public CaseGroupService(
        IConversationRepository conversations, IConversationAccess access, ICallerIdentityResolver identity, IDateTimeProvider clock,
        IPeopleChatService? people = null)
    {
        _people = people;
        _conversations = conversations;
        _access = access;
        _identity = identity;
        _clock = clock;
    }

    public async Task<Conversation?> EnsureAsync(Guid tenantId, ItemRef item, Guid? actorEmployeeId, CancellationToken ct)
    {
        var subject = await _access.ResolveSubjectAsync(tenantId, item, ct);
        if (subject is null)
            return null;
        var now = _clock.UtcNow;
        var group = await _conversations.GetBySubjectAsync(tenantId, item, ct)
            ?? await _conversations.GetOrCreateAsync(new Conversation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectSource = item.Source, SubjectKind = item.Kind, SubjectId = item.Id,
                CreatedAt = now,
            }, ct);

        var markerId = PeopleChatNames.DeterministicId($"case-group:{group.Id:N}");
        var isNew = !(await _conversations.ListExistingMessageIdsAsync(tenantId, [markerId], ct)).Contains(markerId);

        // Base people (requester, approvers) are members through the subject; their rows hold read markers and put the
        // group in their chat list. People of the 1-on-1 chats that carry the card come along as added members.
        var cards = await _conversations.ListCaseCardsAsync(tenantId, item, ct);
        var chatIds = cards.Select(c => c.ConversationId).Where(id => id != group.Id).Distinct().ToList();
        var chatMembers = await _conversations.ListActiveMemberIdsAsync(tenantId, chatIds, ct);
        foreach (var employeeId in subject.BaseParticipantEmployeeIds)
            await AddRowAsync(tenantId, group.Id, employeeId, ConversationJoinReasons.Base, actorEmployeeId, now, ct);
        foreach (var employeeId in chatMembers.Values.SelectMany(ids => ids).Distinct().Except(subject.BaseParticipantEmployeeIds))
            await AddRowAsync(tenantId, group.Id, employeeId, ConversationJoinReasons.Added, actorEmployeeId, now, ct);

        if (!isNew)
            return group;

        await _conversations.AddMessageIfMissingAsync(System(tenantId, group.Id, markerId, now,
            $"Group started for {subject.Title}",
            new { type = PeopleChatEvents.CaseGroup, by = actorEmployeeId, source = item.Source, kind = item.Kind, id = item.Id, title = subject.Title }), ct);
        await _conversations.AddMessageIfMissingAsync(new ConversationMessage
        {
            Id = CaseCards.CardId(group.Id, item), TenantId = tenantId, ConversationId = group.Id, Kind = ConversationMessageKinds.Case,
            BodyHtml = WebUtility.HtmlEncode(subject.Title), SystemEventJson = CaseCards.CardJson(item, subject.Title), CreatedAt = now.AddTicks(1),
        }, ct);
        foreach (var chatId in chatIds)
        {
            await _conversations.AddMessageIfMissingAsync(System(tenantId, chatId, PeopleChatNames.DeterministicId($"case-moved:{chatId:N}:{CaseCards.Key(item)}"), now,
                $"Continued in a group: {subject.Title}",
                new { type = PeopleChatEvents.CaseMoved, conversationId = group.Id, source = item.Source, kind = item.Kind, id = item.Id, title = subject.Title }), ct);
            await _conversations.TouchAsync(tenantId, chatId, now, ct);
        }
        await _conversations.TouchAsync(tenantId, group.Id, now.AddTicks(1), ct);
        return group;
    }

    public async Task<Conversation?> FindAsync(Guid tenantId, ItemRef item, CancellationToken ct)
    {
        if (await _conversations.GetBySubjectAsync(tenantId, item, ct) is not { } group)
            return null;
        var markerId = PeopleChatNames.DeterministicId($"case-group:{group.Id:N}");
        return (await _conversations.ListExistingMessageIdsAsync(tenantId, [markerId], ct)).Contains(markerId) ? group : null;
    }

    public async Task PlaceEscalationInDirectChatsAsync(
        Guid tenantId, ItemRef item, Guid subjectEmployeeId, IReadOnlyList<Guid> targetEmployeeIds, Guid? actorEmployeeId,
        string? note, CancellationToken ct)
    {
        if (_people is null || await _access.ResolveSubjectAsync(tenantId, item, ct) is not { } subject)
            return;
        var now = _clock.UtcNow;
        var people = (await _identity.ResolveIdentitiesByEmployeeIdAsync(
                tenantId, targetEmployeeIds.Append(actorEmployeeId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList(), ct))
            .ToDictionary(p => p.Key, p => p.Value.Name);
        var by = actorEmployeeId is { } actor && people.TryGetValue(actor, out var actorName) ? $" by {actorName}" : "";
        var reason = string.IsNullOrWhiteSpace(note) ? "" : $": {note.Trim()}";
        var targets = targetEmployeeIds.Where(id => id != subjectEmployeeId).Distinct().ToList();
        var targetNames = string.Join(", ", targets.Select(id => people.GetValueOrDefault(id, "a reviewer")));

        // Where the case was so far: those chats say where it went.
        var newChats = new HashSet<Guid>();
        foreach (var target in targets)
        {
            var direct = await _people.GetOrCreateDirectAsync(tenantId, subjectEmployeeId, target, ct);
            newChats.Add(direct.Id);
            await _conversations.AddMessageIfMissingAsync(new ConversationMessage
            {
                Id = CaseCards.CardId(direct.SubjectId, item), TenantId = tenantId, ConversationId = direct.Id, Kind = ConversationMessageKinds.Case,
                BodyHtml = WebUtility.HtmlEncode(subject.Title), SystemEventJson = CaseCards.CardJson(item, subject.Title), CreatedAt = now,
            }, ct);
            await _conversations.AddMessageIfMissingAsync(System(tenantId, direct.Id, Guid.NewGuid(), now.AddTicks(1),
                $"Escalated to {people.GetValueOrDefault(target, "a reviewer")}{by}{reason}",
                new { type = PeopleChatEvents.Escalated, by = actorEmployeeId, to = new[] { target }, source = item.Source, kind = item.Kind, id = item.Id }), ct);
            await _conversations.TouchAsync(tenantId, direct.Id, now.AddTicks(1), ct);
        }
        foreach (var card in await _conversations.ListCaseCardsAsync(tenantId, item, ct))
        {
            if (newChats.Contains(card.ConversationId))
                continue;
            await _conversations.AddMessageIfMissingAsync(System(tenantId, card.ConversationId, Guid.NewGuid(), now,
                $"Escalated to {targetNames}{by}{reason}",
                new { type = PeopleChatEvents.Escalated, by = actorEmployeeId, to = targets, source = item.Source, kind = item.Kind, id = item.Id }), ct);
            await _conversations.TouchAsync(tenantId, card.ConversationId, now, ct);
        }
    }

    public async Task AddNextApproverAsync(Guid tenantId, Conversation group, Guid employeeId, CancellationToken ct)
    {
        var rows = await _conversations.ListParticipantsAsync(tenantId, group.Id, ct);
        var lineId = PeopleChatNames.DeterministicId($"next-approver:{group.Id:N}:{employeeId:N}");
        if ((await _conversations.ListExistingMessageIdsAsync(tenantId, [lineId], ct)).Contains(lineId))
            return;
        var now = _clock.UtcNow;
        // A base approver already has a row from EnsureAsync; the line is what tells everyone why they are here.
        if (rows.All(r => r.EmployeeId != employeeId))
            await AddRowAsync(tenantId, group.Id, employeeId, ConversationJoinReasons.Added, null, now, ct);
        var name = (await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, [employeeId], ct)).GetValueOrDefault(employeeId)?.Name ?? "a reviewer";
        await _conversations.AddMessageIfMissingAsync(System(tenantId, group.Id, lineId, now, $"Next approver: {name}",
            new { type = PeopleChatEvents.NextApprover, employee = employeeId }), ct);
        await _conversations.TouchAsync(tenantId, group.Id, now, ct);
    }

    private async Task AddRowAsync(
        Guid tenantId, Guid conversationId, Guid employeeId, string reason, Guid? addedBy, DateTimeOffset now, CancellationToken ct)
        => await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, EmployeeId = employeeId, JoinReason = reason,
            AddedByEmployeeId = addedBy, AddedAt = now, CreatedAt = now,
        }, ct);

    private static ConversationMessage System(Guid tenantId, Guid conversationId, Guid id, DateTimeOffset at, string body, object payload)
        => new()
        {
            Id = id, TenantId = tenantId, ConversationId = conversationId, Kind = ConversationMessageKinds.System,
            BodyHtml = WebUtility.HtmlEncode(body), SystemEventJson = JsonSerializer.Serialize(payload), CreatedAt = at,
        };
}

/// <summary>The case card message of an item in a chat, and its "resolved" line.</summary>
public static class CaseCards
{
    /// <summary>Statuses that end a case, across the modules. "Not pending" is not enough: IsPending is per viewer (a
    /// step-1 approver sees a two-step request as not pending on them while step 2 is still open).</summary>
    private static readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "approved", "rejected", "cancelled", "canceled", "resolved", "declined", "expired", "accepted", "charged", "stale", "skipped",
        "withdrawn", "closed", "revoked", "completed", "dismissed", "outdated",
    };

    /// <summary>The case itself is over (decided, closed, cancelled), whoever looks at it.</summary>
    public static bool IsClosed(string status) => ClosedStatuses.Contains(status);

    public static string Key(ItemRef item) => $"{item.Source}:{item.Kind}:{item.Id:N}";

    public static Guid CardId(Guid conversationOrPairId, ItemRef item)
        => PeopleChatNames.DeterministicId($"case:{conversationOrPairId:N}:{Key(item)}");

    public static Guid ResolvedId(Guid conversationId, ItemRef item)
        => PeopleChatNames.DeterministicId($"case-resolved:{conversationId:N}:{Key(item)}");

    /// <summary>The card's event JSON. <paramref name="forwarded"/>: a copy someone sent into another chat.</summary>
    public static string CardJson(ItemRef item, string title, bool forwarded = false)
        => forwarded
            ? JsonSerializer.Serialize(new { type = "case", source = item.Source, kind = item.Kind, id = item.Id, title, forwarded = true })
            : JsonSerializer.Serialize(new { type = "case", source = item.Source, kind = item.Kind, id = item.Id, title });
}
