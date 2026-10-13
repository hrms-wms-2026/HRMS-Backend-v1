using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public static class ConversationJoinReasons
{
    /// <summary>Resolved from the subject (sender/receiver/alert recipient) at the time the chat was opened.</summary>
    public const string Base = "base";
    /// <summary>Added by another participant; can view and chat but never decide.</summary>
    public const string Added = "added";
}

public class ConversationParticipant : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid EmployeeId { get; set; }
    public string JoinReason { get; set; } = ConversationJoinReasons.Base;
    public Guid? AddedByEmployeeId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
}
