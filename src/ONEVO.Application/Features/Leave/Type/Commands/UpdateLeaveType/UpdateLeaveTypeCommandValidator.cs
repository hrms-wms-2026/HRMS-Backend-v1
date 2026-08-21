using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public class UpdateLeaveTypeCommandValidator : AbstractValidator<UpdateLeaveTypeCommand>
{
    public UpdateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Leave type name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Category).Must(c => LeaveTypeCategories.All.Contains(c))
            .WithMessage("Category must be one of: annual, sick, maternity, paternity, compassionate, unpaid, custom.");

        RuleFor(x => x.ApplicableGender).Must(g => LeaveGenderRestrictions.AllValues.Contains(g))
            .WithMessage("Applicable gender must be one of: all, male, female.");

        RuleFor(x => x.DefaultDaysPerYear).GreaterThan(0)
            .WithMessage("Default days per year must be positive.");

        RuleFor(x => x.MaxCarryForwardDays)
            .LessThanOrEqualTo(x => x.DefaultDaysPerYear)
            .When(x => x.CarryForwardAllowed && x.MaxCarryForwardDays.HasValue)
            .WithMessage("Carry-forward days cannot exceed default annual entitlement.");

        RuleFor(x => x.CarryForwardExpiryMonths)
            .InclusiveBetween(1, 12)
            .When(x => x.CarryForwardAllowed && x.CarryForwardExpiryMonths.HasValue)
            .WithMessage("Carry-forward expiry must be between 1 and 12 months.");

        RuleFor(x => x.DocumentRequiredAfterDays)
            .GreaterThanOrEqualTo(1)
            .When(x => x.RequiresDocument && x.DocumentRequiredAfterDays.HasValue)
            .WithMessage("Document-required threshold must be at least 1 day.");

        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
    }
}
