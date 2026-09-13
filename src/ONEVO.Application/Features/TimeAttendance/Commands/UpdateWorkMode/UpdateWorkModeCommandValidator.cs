using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;

public class UpdateWorkModeCommandValidator : AbstractValidator<UpdateWorkModeCommand>
{
    public UpdateWorkModeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Work mode name is required.")
            .Must(n => n.Trim().Length <= 120).WithMessage("Work mode name cannot exceed 120 characters.");
    }
}
