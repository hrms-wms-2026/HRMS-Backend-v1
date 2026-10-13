using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Tests.Unit.Features.Conversations;

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    public List<Conversation> Conversations { get; } = new();
    public List<ConversationParticipant> Participants { get; } = new();
    public List<ConversationMessage> Messages { get; } = new();
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
            var row = Participants.FirstOrDefault(p => p.ConversationId == conversation.Id && p.EmployeeId == employeeId);
            if (row is null) continue;
            var lastRead = row.LastReadAt;
            var count = Messages.Count(m => m.ConversationId == conversation.Id && m.AuthorEmployeeId != employeeId
                && (lastRead == null || m.CreatedAt > lastRead));
            if (count > 0) result[subject] = count;
        }
        return Task.FromResult<IReadOnlyDictionary<ItemRef, int>>(result);
    }

    public Task<IReadOnlyList<ItemRef>> ListAddedSubjectsAsync(Guid tenantId, Guid employeeId, string source, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ItemRef>>((
            from p in Participants
            where p.TenantId == tenantId && p.EmployeeId == employeeId && p.JoinReason == ConversationJoinReasons.Added
            join c in Conversations on p.ConversationId equals c.Id
            where c.SubjectSource == source
            select new ItemRef(c.SubjectSource, c.SubjectKind, c.SubjectId)).ToList());

    public void Update(Conversation conversation) { }
    public void UpdateParticipant(ConversationParticipant participant) { }

    private static bool Matches(Conversation c, ItemRef subject)
        => c.SubjectSource == subject.Source && c.SubjectKind == subject.Kind && c.SubjectId == subject.Id;
}
