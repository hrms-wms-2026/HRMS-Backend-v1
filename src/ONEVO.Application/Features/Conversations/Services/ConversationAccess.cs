using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>An authorized view of one conversation for one caller. <see cref="ParticipantIds"/> is the
/// current membership: today's base participants (resolved from the subject) plus everyone added.
/// A base row left behind by a former approver does not count.</summary>
public sealed record ConversationContext(
    Conversation Conversation, ItemRef Subject, ConversationSubject SubjectInfo,
    IReadOnlyList<ConversationParticipant> Rows, IReadOnlyList<Guid> ParticipantIds, Guid Caller)
{
    public bool IsBase(Guid employeeId) => SubjectInfo.BaseParticipantEmployeeIds.Contains(employeeId);
    public ConversationParticipant? RowOf(Guid employeeId) => Rows.FirstOrDefault(r => r.EmployeeId == employeeId);
}

public interface IConversationAccess
{
    /// <summary>Null when no module owns the source, or the subject does not exist (fails closed).</summary>
    Task<ConversationSubject?> ResolveSubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct);

    /// <summary>Null when the caller is neither a current base participant nor an added participant.</summary>
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

    public async Task<ConversationContext?> AuthorizeAsync(
        Guid tenantId, Conversation conversation, ConversationSubject subjectInfo, Guid caller, CancellationToken ct)
    {
        var rows = await _conversations.ListParticipantsAsync(tenantId, conversation.Id, ct);
        var baseIds = subjectInfo.BaseParticipantEmployeeIds;
        var added = rows.Where(r => r.JoinReason == ConversationJoinReasons.Added).OrderBy(r => r.AddedAt).Select(r => r.EmployeeId);
        var participantIds = baseIds.Concat(added).Distinct().ToList();
        if (!participantIds.Contains(caller))
            return null;

        var subject = new ItemRef(conversation.SubjectSource, conversation.SubjectKind, conversation.SubjectId);
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
}
