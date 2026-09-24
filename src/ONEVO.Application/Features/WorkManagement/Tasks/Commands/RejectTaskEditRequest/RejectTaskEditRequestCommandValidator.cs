using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskEditRequest;

public class RejectTaskEditRequestCommandValidator : AbstractValidator<RejectTaskEditRequestCommand>
{
    public RejectTaskEditRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEqual(Guid.Empty).WithMessage("Request is required.");
        RuleFor(x => x.Comment).NotEmpty().WithMessage("A decision comment is required when rejecting.");
    }
}
