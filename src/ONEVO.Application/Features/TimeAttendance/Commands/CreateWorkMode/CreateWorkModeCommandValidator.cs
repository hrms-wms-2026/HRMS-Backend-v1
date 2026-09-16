using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;

public class CreateWorkModeCommandValidator : AbstractValidator<CreateWorkModeCommand>
{
    public CreateWorkModeCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Work mode name is required.")
            .Must(n => n.Trim().Length <= 120).WithMessage("Work mode name cannot exceed 120 characters.");
        RuleFor(x => x)
            .Must(x => !(x.SelfRegistersLocation && x.AllowsDailyLocationChoice))
            .WithMessage("A work mode cannot both self-register a fixed location and let the employee choose daily - pick one.");
    }
}
