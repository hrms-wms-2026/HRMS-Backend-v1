using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public static class TaskCommentViewModelMapper
{
    public static TaskCommentViewModel ToViewModel(this TaskCommentResponse response) => new(
        response.Id, response.TaskId, response.ParentCommentId, response.EmployeeId, response.EmployeeName, response.Content,
        response.IsEdited, response.IsDeleted, response.CreatedAt,
        response.Reactions.Select(r => new TaskCommentReactionViewModel(
            r.Emoji, r.EmployeeIds, r.Reactors.Select(x => new TaskCommentReactorViewModel(x.EmployeeId, x.Name)).ToList())).ToList(),
        response.Attachments.Select(a => new TaskAttachmentViewModel(a.FileId, a.FileName, a.FileSizeBytes, a.ContentType)).ToList(),
        response.Replies.Select(ToViewModel).ToList());
}
