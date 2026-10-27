using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>A participant-level hide. The message remains available to the other participants and to audit/export.</summary>
public sealed class ConversationMessageHide : BaseEntity
{
    public Guid MessageId { get; set; }
    public Guid EmployeeId { get; set; }
}
