using FluentValidation;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Type.Validation;

/// <summary>The fields CreateLeaveTypeCommand and UpdateLeaveTypeCommand share.</summary>
public interface ILeaveTypeInput
{
    string Name { get; }
    string Color { get; }
    string? Icon { get; }
    string ApplicableGender { get; }
    bool RequiresDocument { get; }
    decimal? DocumentRequiredAfterHours { get; }
}

/// <summary>Rules shared by the create and update leave type validators.</summary>
public abstract class LeaveTypeInputValidator<T> : AbstractValidator<T> where T : ILeaveTypeInput
{
    protected LeaveTypeInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Leave type name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Color).Must(LeaveCalendarOptions.IsValidHexColor)
            .WithMessage("Colour must be a #RRGGBB hex colour.");

        RuleFor(x => x.Icon).Must(EntityIconRules.IsValid)
            .WithMessage(EntityIconRules.InvalidMessage);

        RuleFor(x => x.ApplicableGender).Must(g => LeaveGenderRestrictions.AllValues.Contains(g))
            .WithMessage("Applicable gender must be one of: all, male, female.");

        RuleFor(x => x.DocumentRequiredAfterHours)
            .GreaterThan(0)
            .When(x => x.RequiresDocument && x.DocumentRequiredAfterHours.HasValue)
            .WithMessage("Document-required threshold must be more than 0 hours.");
    }
}
