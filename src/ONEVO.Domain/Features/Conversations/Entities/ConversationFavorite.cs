using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>A chat one employee pinned to the top of its section in their inbox. Only that employee sees it.</summary>
public class ConversationFavorite : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid EmployeeId { get; set; }
}
