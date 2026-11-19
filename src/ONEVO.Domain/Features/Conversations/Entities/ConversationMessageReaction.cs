using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>One person's emoji reaction on one chat message. Unique per (MessageId, EmployeeId): reacting again
/// replaces the emoji, so each person has at most one reaction on a message.</summary>
public class ConversationMessageReaction : BaseEntity
{
    public Guid MessageId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}
