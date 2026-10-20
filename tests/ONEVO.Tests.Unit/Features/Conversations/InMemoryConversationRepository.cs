using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Tests.Unit.Features.Conversations;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    public List<Conversation> Conversations { get; } = new();
    public List<ConversationParticipant> Participants { get; } = new();
    public List<ConversationMessage> Messages { get; } = new();
    public List<ConversationMessageRevision> Revisions { get; } = new();
    public List<ConversationMessageAttachment> Attachments { get; } = new();
    public List<ConversationMessageMention> Mentions { get; } = new();

    public Task<Conversation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Conversations.FirstOrDefault(c => c.TenantId == tenantId && c.Id == id));

    public Task<Conversation?> GetBySubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct = default)
        => Task.FromResult(Conversations.FirstOrDefault(c => c.TenantId == tenantId && Matches(c, subject)));

    public Task<Conversation> GetOrCreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        var existing = Conversations.FirstOrDefault(c => c.TenantId == conversation.TenantId
            && Matches(c, new ItemRef(conversation.SubjectSource, conversation.SubjectKind, conversation.SubjectId)));
        if (existing is not null)
            return Task.FromResult(existing);
        Conversations.Add(conversation);
        return Task.FromResult(conversation);
    }

    public Task<IReadOnlyList<ConversationParticipant>> ListParticipantsAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationParticipant>>(
            Participants.Where(p => p.TenantId == tenantId && p.ConversationId == conversationId).ToList());

    public Task<bool> AddParticipantIfMissingAsync(ConversationParticipant participant, CancellationToken ct = default)
    {
        if (Participants.Any(p => p.ConversationId == participant.ConversationId && p.EmployeeId == participant.EmployeeId))
            return Task.FromResult(false);
        Participants.Add(participant);
        return Task.FromResult(true);
    }

    public Task AddMessageAsync(ConversationMessage message, IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default)
    {
        Messages.Add(message);
        Mentions.AddRange(mentions);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(
        Guid tenantId, Guid conversationId, DateTimeOffset? before, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && (before == null || m.CreatedAt < before))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take).ToList());

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListMentionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(Mentions
            .Where(m => m.TenantId == tenantId && messageIds.Contains(m.MessageId))
            .GroupBy(m => m.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(m => m.EmployeeId).ToList()));

    public Task<IReadOnlyDictionary<ItemRef, int>> CountUnreadAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> subjects, CancellationToken ct = default)
    {
        var result = new Dictionary<ItemRef, int>();
        foreach (var subject in subjects)
        {
            var conversation = Conversations.FirstOrDefault(c => c.TenantId == tenantId && Matches(c, subject));
            if (conversation is null) continue;
            var row = Participants.FirstOrDefault(p => p.ConversationId == conversation.Id && p.EmployeeId == employeeId && p.IsActive);
            if (row is null) continue;
            var lastRead = row.LastReadAt;
            var count = Messages.Count(m => m.ConversationId == conversation.Id && m.AuthorEmployeeId != employeeId
                && m.Kind != ConversationMessageKinds.Evidence && (lastRead == null || m.CreatedAt > lastRead));
            if (count > 0) result[subject] = count;
        }
        return Task.FromResult<IReadOnlyDictionary<ItemRef, int>>(result);
    }

    public Task<IReadOnlyList<ItemRef>> ListAddedSubjectsAsync(Guid tenantId, Guid employeeId, string source, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ItemRef>>((
            from p in Participants
            where p.TenantId == tenantId && p.EmployeeId == employeeId && p.JoinReason == ConversationJoinReasons.Added && p.IsActive
            join c in Conversations on p.ConversationId equals c.Id
            where c.SubjectSource == source
            select new ItemRef(c.SubjectSource, c.SubjectKind, c.SubjectId)).ToList());

    public Task<IReadOnlyList<ConversationMessage>> ListSystemMessagesAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && m.Kind == ConversationMessageKinds.System)
            .OrderBy(m => m.CreatedAt).ToList());

    public Task<IReadOnlyList<ConversationMessage>> ListMessagesByIdAsync(
        Guid tenantId, Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && messageIds.Contains(m.Id)).ToList());

    public Task<bool> AddMessageIfMissingAsync(ConversationMessage message, CancellationToken ct = default)
    {
        if (Messages.Any(m => m.Id == message.Id))
            return Task.FromResult(false);
        Messages.Add(message);
        return Task.FromResult(true);
    }

    public Task<ConversationMessage?> GetMessageAsync(Guid tenantId, Guid messageId, CancellationToken ct = default)
        => Task.FromResult(Messages.FirstOrDefault(m => m.TenantId == tenantId && m.Id == messageId));

    public void UpdateMessage(ConversationMessage message) { }

    public Task AddMentionsAsync(IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default)
    {
        Mentions.AddRange(mentions);
        return Task.CompletedTask;
    }

    public Task AddRevisionAsync(ConversationMessageRevision revision, CancellationToken ct = default)
    {
        Revisions.Add(revision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationMessageRevision>> ListRevisionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessageRevision>>(Revisions
            .Where(r => r.TenantId == tenantId && messageIds.Contains(r.MessageId)).OrderBy(r => r.RevisedAt).ToList());

    public Task AddAttachmentsAsync(IReadOnlyCollection<ConversationMessageAttachment> attachments, CancellationToken ct = default)
    {
        Attachments.AddRange(attachments);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>> ListAttachmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>>(Attachments
            .Where(a => a.TenantId == tenantId && messageIds.Contains(a.MessageId))
            .GroupBy(a => a.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ConversationMessageAttachment>)g.ToList()));

    public Task<ConversationMessageAttachment?> GetAttachmentAsync(Guid tenantId, Guid attachmentId, CancellationToken ct = default)
        => Task.FromResult(Attachments.FirstOrDefault(a => a.TenantId == tenantId && a.Id == attachmentId));

    public void Update(Conversation conversation) { }
    public void UpdateParticipant(ConversationParticipant participant) { }

    public Task<IReadOnlyList<MemberConversation>> ListMemberConversationsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemberConversation>>(Participants
            .Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId && p.RemovedAt == null && p.LeftAt == null)
            .Select(p => Conversations.First(c => c.Id == p.ConversationId) is var c
                ? new MemberConversation(c, p.LastReadAt, Messages
                    .Where(m => m.ConversationId == c.Id && m.Kind != ConversationMessageKinds.Evidence)
                    .OrderByDescending(m => m.CreatedAt).FirstOrDefault())
                : null!)
            .ToList());

    public Task<IReadOnlyDictionary<Guid, int>> CountUnreadByConversationAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, int>>(conversationIds.ToDictionary(id => id, id =>
        {
            var row = Participants.FirstOrDefault(p => p.ConversationId == id && p.EmployeeId == employeeId);
            return Messages.Count(m => m.ConversationId == id && m.AuthorEmployeeId != employeeId && m.Kind != ConversationMessageKinds.Evidence
                && (row?.LastReadAt is null || m.CreatedAt > row.LastReadAt));
        }));

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListActiveMemberIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(Participants
            .Where(p => conversationIds.Contains(p.ConversationId) && p.RemovedAt == null && p.LeftAt == null)
            .GroupBy(p => p.ConversationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(p => p.EmployeeId).ToList()));

    public Task<IReadOnlySet<Guid>> ListExistingMessageIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<Guid>>(Messages.Where(m => messageIds.Contains(m.Id)).Select(m => m.Id).ToHashSet());

    public Task<IReadOnlyList<ConversationMessage>> ListCaseCardsAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages
            .Where(m => m.Kind == ConversationMessageKinds.Case && m.SystemEventJson != null && m.SystemEventJson.Contains(item.Id.ToString())
                && m.SystemEventJson.Contains($"\"source\":\"{item.Source}\"") && m.SystemEventJson.Contains($"\"kind\":\"{item.Kind}\""))
            .OrderBy(m => m.CreatedAt).ToList());

    public Task<IReadOnlyDictionary<Guid, ConversationMessage>> LatestSystemEventsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, IReadOnlyCollection<string> eventTypes, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, ConversationMessage>>(Messages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Kind == ConversationMessageKinds.System && m.SystemEventJson != null
                && eventTypes.Any(t => m.SystemEventJson.StartsWith("{\"type\":\"" + t + "\"", StringComparison.Ordinal)))
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First()));

    public Task TouchAsync(Guid tenantId, Guid conversationId, DateTimeOffset at, CancellationToken ct = default)
    {
        var c = Conversations.FirstOrDefault(x => x.Id == conversationId);
        if (c is not null && (c.LastMessageAt is null || c.LastMessageAt < at))
            c.LastMessageAt = at;
        return Task.CompletedTask;
    }

    private static bool Matches(Conversation c, ItemRef subject)
        => c.SubjectSource == subject.Source && c.SubjectKind == subject.Kind && c.SubjectId == subject.Id;
}
