using FluentValidation;
using ONEVO.Application.Features.Leave.Type.Validation;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public class CreateLeaveTypeCommandValidator : LeaveTypeInputValidator<CreateLeaveTypeCommand>
{
    public CreateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .WithMessage("Leave type code is required and cannot exceed 20 characters.");
    }
}
