using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.WorkManagement.Tasks.Validation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;

public sealed class EditTaskCommentCommandValidator : AbstractValidator<EditTaskCommentCommand>
{
    public EditTaskCommentCommandValidator()
    {
        RuleFor(x => x.Content).ValidRichText(TaskCommentInputLimits.ContentMaxLength);
        RuleFor(x => x.AttachmentFileIds).ValidIdList(TaskCommentInputLimits.MaxAttachments);
    }
}
