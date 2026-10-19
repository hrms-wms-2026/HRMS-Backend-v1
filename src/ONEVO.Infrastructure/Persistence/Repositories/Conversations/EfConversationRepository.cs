using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Conversations;

public class EfConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _db;

    public EfConversationRepository(ApplicationDbContext db) => _db = db;

    public async Task<Conversation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Conversations.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public async Task<Conversation?> GetBySubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct = default)
        => await _db.Conversations.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.SubjectSource == subject.Source
            && c.SubjectKind == subject.Kind && c.SubjectId == subject.Id, ct);

    public async Task<Conversation> GetOrCreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        _db.Conversations.Add(conversation);
        try
        {
            await _db.SaveChangesAsync(ct);
            return conversation;
        }
        catch (DbUpdateException)
        {
            // Lost the race on the (tenant, subject) unique index: use the winner's row.
            _db.Entry(conversation).State = EntityState.Detached;
            var subject = new ItemRef(conversation.SubjectSource, conversation.SubjectKind, conversation.SubjectId);
            var winner = await GetBySubjectAsync(conversation.TenantId, subject, ct);
            if (winner is null)
                throw;
            return winner;
        }
    }

    public async Task<IReadOnlyList<ConversationParticipant>> ListParticipantsAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
        => await _db.ConversationParticipants
            .Where(p => p.TenantId == tenantId && p.ConversationId == conversationId)
            .OrderBy(p => p.AddedAt)
            .ToListAsync(ct);

    public async Task<bool> AddParticipantIfMissingAsync(ConversationParticipant participant, CancellationToken ct = default)
    {
        var exists = await _db.ConversationParticipants.AnyAsync(
            p => p.ConversationId == participant.ConversationId && p.EmployeeId == participant.EmployeeId, ct);
        if (exists)
            return false;

        _db.ConversationParticipants.Add(participant);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the race on the (conversation, employee) unique index: the row exists, which is the goal.
            _db.Entry(participant).State = EntityState.Detached;
            var presentNow = await _db.ConversationParticipants.AsNoTracking().AnyAsync(
                p => p.ConversationId == participant.ConversationId && p.EmployeeId == participant.EmployeeId, ct);
            if (!presentNow)
                throw;
            return false;
        }
    }

    public async Task AddMessageAsync(ConversationMessage message, IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default)
    {
        await _db.ConversationMessages.AddAsync(message, ct);
        if (mentions.Count > 0)
            await _db.ConversationMessageMentions.AddRangeAsync(mentions, ct);
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(
        Guid tenantId, Guid conversationId, DateTimeOffset? before, int take, CancellationToken ct = default)
    {
        var query = _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId);
        if (before is { } cutoff)
            query = query.Where(m => m.CreatedAt < cutoff);
        return await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListMessagesByIdAsync(
        Guid tenantId, Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return [];
        var ids = messageIds.ToList();
        return await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && ids.Contains(m.Id))
            .ToListAsync(ct);
    }

    public async Task<bool> AddMessageIfMissingAsync(ConversationMessage message, CancellationToken ct = default)
    {
        // The auditing interceptor stamps CreatedAt with "now" on insert; this row's time is its place in the chat.
        var placedAt = message.CreatedAt;
        var inserted = false;
        if (!await _db.ConversationMessages.AnyAsync(m => m.Id == message.Id, ct))
        {
            _db.ConversationMessages.Add(message);
            try
            {
                await _db.SaveChangesAsync(ct);
                inserted = true;
            }
            catch (DbUpdateException)
            {
                // Lost the race on the primary key: the row exists, which is the goal.
                _db.Entry(message).State = EntityState.Detached;
                if (!await _db.ConversationMessages.AsNoTracking().AnyAsync(m => m.Id == message.Id, ct))
                    throw;
            }
        }

        // Also puts back rows saved before this fix at the interceptor's time.
        await _db.ConversationMessages
            .Where(m => m.Id == message.Id && m.CreatedAt != placedAt)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.CreatedAt, placedAt), ct);
        if (inserted)
            _db.Entry(message).State = EntityState.Detached;
        return inserted;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListMentionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var ids = messageIds.ToList();
        var rows = await _db.ConversationMessageMentions.AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.MessageId))
            .Select(m => new { m.MessageId, m.EmployeeId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.EmployeeId).ToList());
    }

    public async Task<IReadOnlyDictionary<ItemRef, int>> CountUnreadAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> subjects, CancellationToken ct = default)
    {
        if (subjects.Count == 0)
            return new Dictionary<ItemRef, int>();

        var subjectIds = subjects.Select(s => s.Id).Distinct().ToList();
        var conversations = await _db.Conversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && subjectIds.Contains(c.SubjectId) && c.LastMessageAt != null)
            .Select(c => new { c.Id, c.SubjectSource, c.SubjectKind, c.SubjectId })
            .ToListAsync(ct);
        var wanted = subjects.ToHashSet();
        var byConversation = conversations
            .Select(c => (c.Id, Ref: new ItemRef(c.SubjectSource, c.SubjectKind, c.SubjectId)))
            .Where(c => wanted.Contains(c.Ref))
            .ToDictionary(c => c.Id, c => c.Ref);
        if (byConversation.Count == 0)
            return new Dictionary<ItemRef, int>();

        var conversationIds = byConversation.Keys.ToList();
        // Only conversations the employee is a participant of (has a row - every current base member gets
        // one on the first open): an item listed for someone who cannot open its chat never shows unread.
        var counts = await (
                from m in _db.ConversationMessages.AsNoTracking()
                join p in _db.ConversationParticipants.AsNoTracking() on m.ConversationId equals p.ConversationId
                where m.TenantId == tenantId && conversationIds.Contains(m.ConversationId) && p.EmployeeId == employeeId
                    && p.RemovedAt == null && p.LeftAt == null
                    && m.Kind != ConversationMessageKinds.Evidence
                    && (m.AuthorEmployeeId == null || m.AuthorEmployeeId != employeeId)
                    && (p.LastReadAt == null || m.CreatedAt > p.LastReadAt)
                group m by m.ConversationId into g
                select new { ConversationId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(c => byConversation[c.ConversationId], c => c.Count);
    }

    public async Task<IReadOnlyList<ItemRef>> ListAddedSubjectsAsync(
        Guid tenantId, Guid employeeId, string source, CancellationToken ct = default)
    {
        var rows = await (
                from p in _db.ConversationParticipants.AsNoTracking()
                join c in _db.Conversations.AsNoTracking() on p.ConversationId equals c.Id
                where p.TenantId == tenantId && p.EmployeeId == employeeId && p.JoinReason == ConversationJoinReasons.Added
                    && p.RemovedAt == null && p.LeftAt == null
                    && c.SubjectSource == source
                select new { c.SubjectSource, c.SubjectKind, c.SubjectId })
            .ToListAsync(ct);
        return rows.Select(r => new ItemRef(r.SubjectSource, r.SubjectKind, r.SubjectId)).ToList();
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListSystemMessagesAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
        => await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && m.Kind == ConversationMessageKinds.System)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<ConversationMessage?> GetMessageAsync(Guid tenantId, Guid messageId, CancellationToken ct = default)
        => await _db.ConversationMessages.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == messageId, ct);

    public void UpdateMessage(ConversationMessage message) => _db.ConversationMessages.Update(message);

    public async Task AddMentionsAsync(IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default)
    {
        if (mentions.Count > 0)
            await _db.ConversationMessageMentions.AddRangeAsync(mentions, ct);
    }

    public async Task AddRevisionAsync(ConversationMessageRevision revision, CancellationToken ct = default)
        => await _db.ConversationMessageRevisions.AddAsync(revision, ct);

    public async Task<IReadOnlyList<ConversationMessageRevision>> ListRevisionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return [];
        var ids = messageIds.ToList();
        return await _db.ConversationMessageRevisions.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.MessageId))
            .OrderBy(r => r.RevisedAt)
            .ToListAsync(ct);
    }

    public async Task AddAttachmentsAsync(IReadOnlyCollection<ConversationMessageAttachment> attachments, CancellationToken ct = default)
    {
        if (attachments.Count > 0)
            await _db.ConversationMessageAttachments.AddRangeAsync(attachments, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>> ListAttachmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>();
        var ids = messageIds.ToList();
        var rows = await _db.ConversationMessageAttachments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && ids.Contains(a.MessageId))
            .ToListAsync(ct);
        return rows.GroupBy(a => a.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ConversationMessageAttachment>)g.OrderBy(a => a.CreatedAt).ToList());
    }

    public async Task<ConversationMessageAttachment?> GetAttachmentAsync(Guid tenantId, Guid attachmentId, CancellationToken ct = default)
        => await _db.ConversationMessageAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == attachmentId, ct);

    public void Update(Conversation conversation) => _db.Conversations.Update(conversation);

    public void UpdateParticipant(ConversationParticipant participant) => _db.ConversationParticipants.Update(participant);
}
