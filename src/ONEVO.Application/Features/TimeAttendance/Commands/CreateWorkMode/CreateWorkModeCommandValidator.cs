using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Validation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;

public class CreateWorkModeCommandValidator : WorkModeInputValidator<CreateWorkModeCommand>
{
    public CreateWorkModeCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
    }
}
