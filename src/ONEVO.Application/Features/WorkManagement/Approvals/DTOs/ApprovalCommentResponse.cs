namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public sealed record ApprovalCommentResponse(
    Guid Id, Guid? ParentCommentId, Guid AuthorId, string AuthorName, string Content,
    bool IsEdited, bool CanEdit, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
