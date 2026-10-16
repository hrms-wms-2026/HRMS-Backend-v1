using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Conversations.Entities;

/// <summary>The group chat attached to one inbox item, addressed by (SubjectSource, SubjectKind, SubjectId),
/// e.g. ("work", "approval", requestId). One conversation per subject per tenant.</summary>
public class Conversation : BaseEntity
{
    public string SubjectSource { get; set; } = string.Empty;
    public string SubjectKind { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
}
