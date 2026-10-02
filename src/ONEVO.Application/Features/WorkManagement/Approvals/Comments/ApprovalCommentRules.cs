using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments;

internal static class ApprovalCommentRules
{
    public const int MaxLength = 4000;
    public const string NotFoundMessage = "Request not found.";

    /// <summary>Trimmed content, or the validation error.</summary>
    public static (string? Content, string? Error) Validate(string? content)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return (null, "Comment cannot be empty.");
        if (trimmed.Length > MaxLength) return (null, "Comment is too long.");
        return (trimmed, null);
    }

    public static ApprovalCommentResponse ToResponse(WorkApprovalComment c, IReadOnlyDictionary<Guid, string> names, Guid caller)
        => new(c.Id, c.ParentCommentId, c.EmployeeId, names.GetValueOrDefault(c.EmployeeId) ?? "A teammate", c.Content,
            c.IsEdited, c.EmployeeId == caller, c.CreatedAt, c.UpdatedAt);
}
