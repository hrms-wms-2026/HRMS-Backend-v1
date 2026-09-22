namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskCommentReactionDto(string Emoji, IReadOnlyList<Guid> EmployeeIds);

public sealed record TaskCommentResponse(
    Guid Id, Guid TaskId, Guid? ParentCommentId, Guid EmployeeId, string EmployeeName, string Content,
    bool IsEdited, bool IsDeleted, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskCommentReactionDto> Reactions, IReadOnlyList<TaskAttachmentDto> Attachments,
    IReadOnlyList<TaskCommentResponse> Replies);
