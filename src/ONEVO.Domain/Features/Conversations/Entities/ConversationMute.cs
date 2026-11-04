using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>A chat one employee muted: its unread messages no longer count towards their inbox badge. Only that
/// employee is affected; mentions still notify them.</summary>
public class ConversationMute : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid EmployeeId { get; set; }
}
