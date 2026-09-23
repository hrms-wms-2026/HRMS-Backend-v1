using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>One emoji reaction from one employee on one comment. A user may
/// stack several distinct emoji on the same comment (unique per (CommentId,
/// EmployeeId, Emoji)), but not the same emoji twice.</summary>
public class TaskCommentReaction : BaseEntity
{
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}
