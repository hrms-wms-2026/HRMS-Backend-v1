namespace ONEVO.Application.Features.WorkManagement.Tasks.Validation;

/// <summary>Task 19 bounds for task comments. task_comments.content is nvarchar(max) with no limit in the model, so the
/// cap is chosen: five times a chat message (10,000), room for formatted HTML and inline image tags (images are file
/// references, not data URLs), and far below anything that could be used to fill the database.</summary>
public static class TaskCommentInputLimits
{
    public const int ContentMaxLength = 50_000;
    public const int MaxAttachments = 20;
}
