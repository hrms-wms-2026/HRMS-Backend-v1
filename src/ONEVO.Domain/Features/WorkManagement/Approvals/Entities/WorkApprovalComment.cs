using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

public static class WorkApprovalCommentSubjects
{
    public const string Approval = "approval";
    public const string Invitation = "invitation";
}

/// <summary>A comment (or, when ParentCommentId is set, a reply) on an approval request or a module
/// invitation, so the requester and the approver can negotiate. Replies always target a top-level
/// comment - enforced by the command handler, like TaskComment.</summary>
public class WorkApprovalComment : BaseEntity
{
    public string SubjectType { get; set; } = WorkApprovalCommentSubjects.Approval;
    public Guid SubjectId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsEdited { get; set; }
}
