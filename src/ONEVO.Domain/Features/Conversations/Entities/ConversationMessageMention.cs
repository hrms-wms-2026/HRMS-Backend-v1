using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

public class ConversationMessageMention : BaseEntity
{
    public Guid MessageId { get; set; }
    public Guid EmployeeId { get; set; }
}
