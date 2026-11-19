namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>What a conversation needs to know about its subject, resolved at read time so a
/// transferred approver position moves the chat to the new holder. <see cref="SubjectEmployeeId"/> is the
/// employee an alert or case is about (they may post a justification); null for requests. <see cref="EvidenceAt"/>:
/// the item has evidence the client shows as a message (an identity case's face checks) - its place in the chat.</summary>
public sealed record ConversationSubject(
    IReadOnlyCollection<Guid> BaseParticipantEmployeeIds, string Title, Guid? ProjectId, Guid? SubjectEmployeeId = null,
    DateTimeOffset? EvidenceAt = null);

/// <summary>One per source module (keyed by <see cref="Source"/>). The Conversations feature knows no
/// module; each module plugs its subjects in through this seam.</summary>
public interface IConversationSubjectResolver
{
    string Source { get; }

    /// <summary>Null when the subject does not exist in this tenant (or its kind is unknown).</summary>
    Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct);

    /// <summary>True when the employee may see the item from their own scope without being a base participant
    /// (HR, an escalation target, a project lead). They join the chat as a "viewer" on first open: they can read
    /// and post but never decide. Default: nobody beyond the base and added participants.</summary>
    Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct) => Task.FromResult(false);

    /// <summary>True when the employee reviews the item from an oversight role (HR scope, the case reviewer chain) and
    /// may take the investigation export - narrower than <see cref="CanViewAsync"/>. Default: nobody; a module opts in.</summary>
    Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct) => Task.FromResult(false);
}
