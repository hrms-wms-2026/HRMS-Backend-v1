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

    public async Task<IReadOnlyList<ConversationMessage>> ListVisibleMessagesAsync(
        Guid tenantId, Guid conversationId, Guid employeeId, DateTimeOffset? before, int take, CancellationToken ct = default)
    {
        var hiddenIds = _db.ConversationMessageHides
            .Where(h => h.TenantId == tenantId && h.EmployeeId == employeeId)
            .Select(h => h.MessageId);
        var query = _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && !hiddenIds.Contains(m.Id));
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
                    && !_db.ConversationMessageHides.Any(h => h.MessageId == m.Id && h.EmployeeId == employeeId)
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

    public async Task<ConversationMessageReaction?> GetReactionAsync(Guid tenantId, Guid messageId, Guid employeeId, CancellationToken ct = default)
        => await _db.ConversationMessageReactions
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.MessageId == messageId && r.EmployeeId == employeeId, ct);

    public async Task AddReactionAsync(ConversationMessageReaction reaction, CancellationToken ct = default)
        => await _db.ConversationMessageReactions.AddAsync(reaction, ct);

    public void RemoveReaction(ConversationMessageReaction reaction) => _db.ConversationMessageReactions.Remove(reaction);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageReaction>>> ListReactionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ConversationMessageReaction>>();
        var ids = messageIds.ToList();
        var rows = await _db.ConversationMessageReactions.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.MessageId))
            .ToListAsync(ct);
        // A few rows per message; ordered here (reacted-at = UpdatedAt ?? CreatedAt) rather than in SQL.
        return rows.OrderBy(r => r.UpdatedAt ?? r.CreatedAt).GroupBy(r => r.MessageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ConversationMessageReaction>)g.ToList());
    }

    public async Task AddRevisionAsync(ConversationMessageRevision revision, CancellationToken ct = default)
        => await _db.ConversationMessageRevisions.AddAsync(revision, ct);

    public async Task HideMessageForEmployeeAsync(ConversationMessageHide hide, CancellationToken ct = default)
    {
        var exists = await _db.ConversationMessageHides.AnyAsync(
            h => h.MessageId == hide.MessageId && h.EmployeeId == hide.EmployeeId, ct);
        if (!exists)
            await _db.ConversationMessageHides.AddAsync(hide, ct);
    }

    public Task<bool> IsMessageHiddenForEmployeeAsync(
        Guid tenantId, Guid messageId, Guid employeeId, CancellationToken ct = default)
        => _db.ConversationMessageHides.AnyAsync(
            h => h.TenantId == tenantId && h.MessageId == messageId && h.EmployeeId == employeeId, ct);

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

    public async Task<IReadOnlyList<MemberConversation>> ListMemberConversationsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var rows = await (
                from p in _db.ConversationParticipants.AsNoTracking()
                join c in _db.Conversations.AsNoTracking() on p.ConversationId equals c.Id
                where p.TenantId == tenantId && p.EmployeeId == employeeId && p.RemovedAt == null && p.LeftAt == null
                select new
                {
                    Conversation = c,
                    p.LastReadAt,
                    Latest = _db.ConversationMessages
                        .Where(m => m.ConversationId == c.Id && m.Kind != ConversationMessageKinds.Evidence
                            && !_db.ConversationMessageHides.Any(h => h.MessageId == m.Id && h.EmployeeId == employeeId))
                        .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                        .FirstOrDefault(),
                })
            .ToListAsync(ct);
        return rows.Select(r => new MemberConversation(r.Conversation, r.LastReadAt, r.Latest)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountUnreadByConversationAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default, bool textOnly = false)
    {
        if (conversationIds.Count == 0)
            return new Dictionary<Guid, int>();
        var ids = conversationIds.ToList();
        var counts = await (
                from m in _db.ConversationMessages.AsNoTracking()
                join p in _db.ConversationParticipants.AsNoTracking() on m.ConversationId equals p.ConversationId
                where m.TenantId == tenantId && ids.Contains(m.ConversationId) && p.EmployeeId == employeeId
                    && p.RemovedAt == null && p.LeftAt == null
                    && m.Kind != ConversationMessageKinds.Evidence
                    && (!textOnly || m.Kind == ConversationMessageKinds.Text)
                    && (m.AuthorEmployeeId == null || m.AuthorEmployeeId != employeeId)
                    && (p.LastReadAt == null || m.CreatedAt > p.LastReadAt)
                    && !_db.ConversationMessageHides.Any(h => h.MessageId == m.Id && h.EmployeeId == employeeId)
                group m by m.ConversationId into g
                select new { ConversationId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return counts.ToDictionary(c => c.ConversationId, c => c.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListActiveMemberIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default)
    {
        if (conversationIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        var ids = conversationIds.ToList();
        var rows = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && ids.Contains(p.ConversationId) && p.RemovedAt == null && p.LeftAt == null)
            .OrderBy(p => p.AddedAt)
            .Select(p => new { p.ConversationId, p.EmployeeId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ConversationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.EmployeeId).ToList());
    }

    public async Task<IReadOnlySet<Guid>> ListExistingMessageIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new HashSet<Guid>();
        var ids = messageIds.Distinct().ToList();
        var found = await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);
        return found.ToHashSet();
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListCaseCardsAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
    {
        // A card's event JSON carries the item's id; the kind filter keeps the text match to card rows only. Cards
        // someone forwarded (they have an author) are copies in other chats, not where the case is worked on.
        var id = item.Id.ToString();
        var cards = await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Kind == ConversationMessageKinds.Case && m.AuthorEmployeeId == null && m.SystemEventJson != null
                && m.SystemEventJson.Contains(id))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        return cards.Where(m => CaseRefOf(m) == item).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, ConversationMessage>> LatestSystemEventsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, IReadOnlyCollection<string> eventTypes, CancellationToken ct = default)
    {
        if (conversationIds.Count == 0 || eventTypes.Count == 0)
            return new Dictionary<Guid, ConversationMessage>();
        var ids = conversationIds.ToList();
        // Event JSON is written with "type" first ({"type":"group_renamed",...}), so a prefix match finds the type.
        var prefixes = eventTypes.Select(t => "{\"type\":\"" + t + "\"").ToList();
        // System messages are few (people added, escalations, names), so the type is matched in memory.
        var rows = await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.ConversationId) && m.Kind == ConversationMessageKinds.System
                && m.SystemEventJson != null)
            .ToListAsync(ct);
        return rows.Where(m => prefixes.Any(prefix => m.SystemEventJson!.StartsWith(prefix, StringComparison.Ordinal)))
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
    }

    public async Task<IReadOnlySet<Guid>> ListFavoriteConversationIdsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => (await _db.ConversationFavorites.AsNoTracking()
                .Where(f => f.TenantId == tenantId && f.EmployeeId == employeeId)
                .Select(f => f.ConversationId)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task SetFavoriteAsync(Guid tenantId, Guid employeeId, Guid conversationId, bool favorite, CancellationToken ct = default)
    {
        if (!favorite)
        {
            await _db.ConversationFavorites
                .Where(f => f.TenantId == tenantId && f.EmployeeId == employeeId && f.ConversationId == conversationId)
                .ExecuteDeleteAsync(ct);
            return;
        }
        if (await _db.ConversationFavorites.AnyAsync(f => f.EmployeeId == employeeId && f.ConversationId == conversationId, ct))
            return;
        var row = new ConversationFavorite { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, ConversationId = conversationId };
        _db.ConversationFavorites.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the (employee, conversation) unique index: it is pinned, which is the goal.
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlySet<Guid>> ListMutedConversationIdsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => (await _db.ConversationMutes.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId)
                .Select(m => m.ConversationId)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task SetMutedAsync(Guid tenantId, Guid employeeId, Guid conversationId, bool muted, CancellationToken ct = default)
    {
        if (!muted)
        {
            await _db.ConversationMutes
                .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId && m.ConversationId == conversationId)
                .ExecuteDeleteAsync(ct);
            return;
        }
        if (await _db.ConversationMutes.AnyAsync(m => m.EmployeeId == employeeId && m.ConversationId == conversationId, ct))
            return;
        var row = new ConversationMute { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, ConversationId = conversationId };
        _db.ConversationMutes.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the (employee, conversation) unique index: it is muted, which is the goal.
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    public async Task SetDescriptionAsync(Guid tenantId, Guid conversationId, string? description, CancellationToken ct = default)
        => await _db.Conversations
            .Where(c => c.TenantId == tenantId && c.Id == conversationId)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Description, description), ct);

    public async Task<ConversationMediaPage> ListMediaAsync(
        Guid tenantId, Guid conversationId, Guid employeeId, string? kind, int skip, int take, CancellationToken ct = default)
    {
        var hiddenIds = _db.ConversationMessageHides
            .Where(h => h.TenantId == tenantId && h.EmployeeId == employeeId)
            .Select(h => h.MessageId);
        var files = from a in _db.ConversationMessageAttachments.AsNoTracking()
                    join m in _db.ConversationMessages.AsNoTracking() on a.MessageId equals m.Id
                    where a.TenantId == tenantId && m.TenantId == tenantId && m.ConversationId == conversationId
                        && m.DeletedAt == null && !hiddenIds.Contains(m.Id)
                    select new { Attachment = a, m.AuthorEmployeeId, SentAt = m.CreatedAt };

        var images = await files.CountAsync(f => f.Attachment.ContentType.StartsWith("image/"), ct);
        var videos = await files.CountAsync(f => f.Attachment.ContentType.StartsWith("video/"), ct);
        var audio = await files.CountAsync(f => f.Attachment.ContentType.StartsWith("audio/"), ct);
        var all = await files.CountAsync(ct);
        var counts = new Dictionary<string, int>
        {
            [ConversationMediaKinds.Image] = images,
            [ConversationMediaKinds.Video] = videos,
            [ConversationMediaKinds.Audio] = audio,
            [ConversationMediaKinds.Document] = all - images - videos - audio,
        };

        var filtered = kind switch
        {
            ConversationMediaKinds.Image => files.Where(f => f.Attachment.ContentType.StartsWith("image/")),
            ConversationMediaKinds.Video => files.Where(f => f.Attachment.ContentType.StartsWith("video/")),
            ConversationMediaKinds.Audio => files.Where(f => f.Attachment.ContentType.StartsWith("audio/")),
            ConversationMediaKinds.Document => files.Where(f => !f.Attachment.ContentType.StartsWith("image/")
                && !f.Attachment.ContentType.StartsWith("video/") && !f.Attachment.ContentType.StartsWith("audio/")),
            _ => files,
        };
        var rows = await filtered.OrderByDescending(f => f.SentAt).ThenByDescending(f => f.Attachment.Id).Skip(skip).Take(take).ToListAsync(ct);
        var total = kind is null ? all : counts.GetValueOrDefault(kind);
        return new ConversationMediaPage(rows.Select(r => new ConversationMediaItem(r.Attachment, r.AuthorEmployeeId, r.SentAt)).ToList(), total, counts);
    }

    public async Task<IReadOnlyList<ConversationMessage>> SearchTextMessagesAsync(
        Guid tenantId, Guid conversationId, Guid employeeId, string text, int take, CancellationToken ct = default)
    {
        var hiddenIds = _db.ConversationMessageHides
            .Where(h => h.TenantId == tenantId && h.EmployeeId == employeeId)
            .Select(h => h.MessageId);
        var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return await _db.ConversationMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId && m.Kind == ConversationMessageKinds.Text
                && m.DeletedAt == null && !hiddenIds.Contains(m.Id) && EF.Functions.ILike(m.BodyHtml, pattern, "\\"))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task DeleteMessageIfExistsAsync(Guid tenantId, Guid messageId, CancellationToken ct = default)
        => await _db.ConversationMessages
            .Where(m => m.TenantId == tenantId && m.Id == messageId && m.AuthorEmployeeId == null)
            .ExecuteDeleteAsync(ct);

    public async Task TouchAsync(Guid tenantId, Guid conversationId, DateTimeOffset at, CancellationToken ct = default)
        => await _db.Conversations
            .Where(c => c.TenantId == tenantId && c.Id == conversationId && (c.LastMessageAt == null || c.LastMessageAt < at))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.LastMessageAt, at), ct);

    private static ItemRef? CaseRefOf(ConversationMessage message)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(message.SystemEventJson!);
            var root = doc.RootElement;
            return root.TryGetProperty("source", out var source) && root.TryGetProperty("kind", out var kind)
                && root.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid)
                ? new ItemRef(source.GetString() ?? string.Empty, kind.GetString() ?? string.Empty, guid)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
