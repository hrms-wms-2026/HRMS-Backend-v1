using FluentValidation;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public class CreateLeaveTypeCommandValidator : AbstractValidator<CreateLeaveTypeCommand>
{
    public CreateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Leave type name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .WithMessage("Leave type code is required and cannot exceed 20 characters.");

        RuleFor(x => x.Color).Must(LeaveCalendarOptions.IsValidHexColor)
            .WithMessage("Colour must be a #RRGGBB hex colour.");

        RuleFor(x => x.ApplicableGender).Must(g => LeaveGenderRestrictions.AllValues.Contains(g))
            .WithMessage("Applicable gender must be one of: all, male, female.");

        RuleFor(x => x.DocumentRequiredAfterHours)
            .GreaterThan(0)
            .When(x => x.RequiresDocument && x.DocumentRequiredAfterHours.HasValue)
            .WithMessage("Document-required threshold must be more than 0 hours.");
    }
}
