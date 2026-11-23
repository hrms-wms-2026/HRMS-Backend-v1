using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Validation;

/// <summary>The fields CreateWorkModeCommand and UpdateWorkModeCommand share.</summary>
public interface IWorkModeInput
{
    string Name { get; }
    bool SelfRegistersLocation { get; }
    bool AllowsDailyLocationChoice { get; }
}

/// <summary>Rules shared by the create and update work mode validators.</summary>
public abstract class WorkModeInputValidator<T> : AbstractValidator<T> where T : IWorkModeInput
{
    protected WorkModeInputValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Work mode name is required.")
            .Must(n => n.Trim().Length <= 120).WithMessage("Work mode name cannot exceed 120 characters.");
        RuleFor(x => x)
            .Must(x => !(x.SelfRegistersLocation && x.AllowsDailyLocationChoice))
            .WithMessage("A work mode cannot both self-register a fixed location and let the employee choose daily - pick one.");
    }
}
