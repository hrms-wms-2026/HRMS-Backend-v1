namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskCommentRequest(string Content, IReadOnlyList<Guid>? AttachmentFileIds);
public sealed record EditTaskCommentRequest(string Content, IReadOnlyList<Guid>? AttachmentFileIds);
public sealed record AddTaskCommentReactionRequest(string Emoji);

public sealed record TaskCommentReactorViewModel(Guid EmployeeId, string Name);

public sealed record TaskCommentReactionViewModel(string Emoji, IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<TaskCommentReactorViewModel> Reactors);

public sealed record TaskCommentViewModel(
    Guid Id, Guid TaskId, Guid? ParentCommentId, Guid EmployeeId, string EmployeeName, string Content,
    bool IsEdited, bool IsDeleted, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskCommentReactionViewModel> Reactions, IReadOnlyList<TaskAttachmentViewModel> Attachments,
    IReadOnlyList<TaskCommentViewModel> Replies);
