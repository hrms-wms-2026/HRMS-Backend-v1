using FluentValidation;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public class CreateLeavePolicyCommandValidator : AbstractValidator<CreateLeavePolicyCommand>
{
    public CreateLeavePolicyCommandValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150)
            .WithMessage("Policy name is required and cannot exceed 150 characters.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Rules).NotNull().SetValidator(new LeavePolicyRulesValidator());
        RuleFor(x => x.Assignments).SetValidator(new LeavePolicyTargetsValidator());
    }
}
