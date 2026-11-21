using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.WorkManagement.Tasks.Validation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;

public sealed class CreateTaskCommentCommandValidator : AbstractValidator<CreateTaskCommentCommand>
{
    public CreateTaskCommentCommandValidator()
    {
        RuleFor(x => x.Content).ValidRichText(TaskCommentInputLimits.ContentMaxLength);
        RuleFor(x => x.AttachmentFileIds).ValidIdList(TaskCommentInputLimits.MaxAttachments);
    }
}
