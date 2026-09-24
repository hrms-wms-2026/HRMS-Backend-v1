using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>One emoji reaction from one employee on one comment. Each employee
/// holds at most one reaction per comment (unique per (CommentId, EmployeeId));
/// reacting again with a different emoji replaces it.</summary>
public class TaskCommentReaction : BaseEntity
{
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}
