namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>What a conversation needs to know about its subject, resolved at read time so a
/// transferred approver position moves the chat to the new holder.</summary>
public sealed record ConversationSubject(
    IReadOnlyCollection<Guid> BaseParticipantEmployeeIds, string Title, Guid? ProjectId);

/// <summary>One per source module (keyed by <see cref="Source"/>). The Conversations feature knows no
/// module; each module plugs its subjects in through this seam.</summary>
public interface IConversationSubjectResolver
{
    string Source { get; }

    /// <summary>Null when the subject does not exist in this tenant (or its kind is unknown).</summary>
    Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct);
}
