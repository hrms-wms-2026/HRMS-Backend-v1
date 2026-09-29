using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>
/// A comment (or, when ParentCommentId is set, a reply) on a WorkTask. Replies
/// always target a top-level comment — ParentCommentId never points at another
/// reply, enforced by the command handler, not the schema.
/// Soft delete (IsDeleted/DeletedAt) is inherited from BaseEntity: a deleted
/// comment's row is kept (so replies underneath aren't orphaned) but every API
/// response strips Content once IsDeleted is true.
/// </summary>
public class TaskComment : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsEdited { get; set; }
}
