using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>An authorized view of one conversation for one caller. <see cref="ParticipantIds"/> is the
/// current membership: today's base participants (resolved from the subject), then viewers and added members who
/// are still in (not removed, not left). A base row left behind by a former approver does not count.</summary>
public sealed record ConversationContext(
    Conversation Conversation, ItemRef Subject, ConversationSubject SubjectInfo,
    IReadOnlyList<ConversationParticipant> Rows, IReadOnlyList<Guid> ParticipantIds, Guid Caller)
{
    public bool IsBase(Guid employeeId) => SubjectInfo.BaseParticipantEmployeeIds.Contains(employeeId);

    /// <summary>The employee's active row, if any (a removed or departed member's row is not returned).</summary>
    public ConversationParticipant? RowOf(Guid employeeId) => Rows.FirstOrDefault(r => r.EmployeeId == employeeId && r.IsActive);

    /// <summary>"base", "viewer" or "added" for a current participant.</summary>
    public string JoinReasonOf(Guid employeeId)
        => IsBase(employeeId) ? ConversationJoinReasons.Base : RowOf(employeeId)?.JoinReason ?? ConversationJoinReasons.Viewer;
}

public interface IConversationAccess
{
    /// <summary>Null when no module owns the source, or the subject does not exist (fails closed).</summary>
    Task<ConversationSubject?> ResolveSubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct);

    /// <summary>True when the employee may see the subject from their own scope (they become a viewer).</summary>
    Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct);

    /// <summary>Null when the caller is neither a current base participant, an active viewer or added participant,
    /// nor someone the subject's module lets view the item (who is then recorded as a viewer).</summary>
    Task<ConversationContext?> AuthorizeAsync(Guid tenantId, Conversation conversation, ConversationSubject subjectInfo, Guid caller, CancellationToken ct);

    /// <summary>Loads the tenant's conversation and authorizes the caller; null when either fails.</summary>
    Task<ConversationContext?> LoadAsync(Guid tenantId, Guid conversationId, Guid caller, CancellationToken ct);
}

public sealed class ConversationAccess : IConversationAccess
{
    private readonly IReadOnlyDictionary<string, IConversationSubjectResolver> _resolvers;
    private readonly IConversationRepository _conversations;

    public ConversationAccess(IEnumerable<IConversationSubjectResolver> resolvers, IConversationRepository conversations)
    {
        _resolvers = resolvers.ToDictionary(r => r.Source, StringComparer.Ordinal);
        _conversations = conversations;
    }

    public async Task<ConversationSubject?> ResolveSubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
        => _resolvers.TryGetValue(subject.Source, out var resolver)
            ? await resolver.ResolveAsync(tenantId, subject, ct)
            : null;

    public async Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => _resolvers.TryGetValue(subject.Source, out var resolver) && await resolver.CanViewAsync(tenantId, subject, employeeId, ct);

    public async Task<ConversationContext?> AuthorizeAsync(
        Guid tenantId, Conversation conversation, ConversationSubject subjectInfo, Guid caller, CancellationToken ct)
    {
        var subject = new ItemRef(conversation.SubjectSource, conversation.SubjectKind, conversation.SubjectId);
        var rows = await _conversations.ListParticipantsAsync(tenantId, conversation.Id, ct);
        var participantIds = Members(subjectInfo, rows);
        if (!participantIds.Contains(caller))
        {
            if (!await CanViewAsync(tenantId, subject, caller, ct))
                return null;
            // Seen from their own scope: recorded as a viewer (holds their read marker). A former added member who
            // was removed keeps their old row and still gets in, because their own scope entitles them.
            if (rows.All(r => r.EmployeeId != caller))
            {
                await _conversations.AddParticipantIfMissingAsync(new ConversationParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, EmployeeId = caller,
                    JoinReason = ConversationJoinReasons.Viewer, AddedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
                }, ct);
                rows = await _conversations.ListParticipantsAsync(tenantId, conversation.Id, ct);
            }
            participantIds = participantIds.Append(caller).Distinct().ToList();
        }

        return new ConversationContext(conversation, subject, subjectInfo, rows, participantIds, caller);
    }

    public async Task<ConversationContext?> LoadAsync(Guid tenantId, Guid conversationId, Guid caller, CancellationToken ct)
    {
        var conversation = await _conversations.GetByIdAsync(tenantId, conversationId, ct);
        if (conversation is null)
            return null;
        var subject = new ItemRef(conversation.SubjectSource, conversation.SubjectKind, conversation.SubjectId);
        var subjectInfo = await ResolveSubjectAsync(tenantId, subject, ct);
        return subjectInfo is null ? null : await AuthorizeAsync(tenantId, conversation, subjectInfo, caller, ct);
    }

    private static List<Guid> Members(ConversationSubject subjectInfo, IReadOnlyList<ConversationParticipant> rows)
    {
        var joined = rows
            .Where(r => r.IsActive && (r.JoinReason == ConversationJoinReasons.Viewer || r.JoinReason == ConversationJoinReasons.Added))
            .OrderBy(r => r.AddedAt)
            .Select(r => r.EmployeeId);
        return subjectInfo.BaseParticipantEmployeeIds.Concat(joined).Distinct().ToList();
    }
}
