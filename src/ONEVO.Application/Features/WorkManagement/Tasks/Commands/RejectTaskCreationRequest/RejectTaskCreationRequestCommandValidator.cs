using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskCreationRequest;

public class RejectTaskCreationRequestCommandValidator : AbstractValidator<RejectTaskCreationRequestCommand>
{
    public RejectTaskCreationRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEqual(Guid.Empty).WithMessage("Request is required.");
        RuleFor(x => x.Comment).NotEmpty().WithMessage("A decision comment is required when rejecting.");
    }
}
