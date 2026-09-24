using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RequestAllocationExtension;

public class RequestAllocationExtensionCommandValidator : AbstractValidator<RequestAllocationExtensionCommand>
{
    public RequestAllocationExtensionCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty);
        RuleFor(x => x.RequestedAdditionalHours).GreaterThan(0).WithMessage("Requested additional hours must be positive.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required.");
    }
}
