using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCommentLogActions
{
    public const string Edited = "edited";
    public const string Deleted = "deleted";
}

/// <summary>
/// Audit row for a comment edit or delete: who + when + which action, never the
/// changed content. Comment creation is never logged here — the comment itself
/// is already visible as the creation event.
/// </summary>
public class TaskCommentLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Action { get; set; } = TaskCommentLogActions.Edited;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
