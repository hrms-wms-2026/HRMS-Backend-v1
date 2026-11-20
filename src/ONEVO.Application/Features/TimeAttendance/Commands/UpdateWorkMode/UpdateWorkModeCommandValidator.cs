using FluentValidation;
using ONEVO.Application.Features.TimeAttendance.Validation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;

public class UpdateWorkModeCommandValidator : WorkModeInputValidator<UpdateWorkModeCommand>
{
    public UpdateWorkModeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
