using FluentValidation;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmployeeJobDetails;

public sealed class UpdateEmployeeJobDetailsCommandValidator : AbstractValidator<UpdateEmployeeJobDetailsCommand>
{
    public UpdateEmployeeJobDetailsCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.EmployeeNumber).NotEmpty().MaximumLength(EmployeeNumberRules.MaxLength);
        RuleFor(x => x.EmploymentTypeCode).NotEmpty();
    }
}
