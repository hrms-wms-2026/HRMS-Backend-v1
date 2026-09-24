using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;

public class UpdateEmergencyContactCommandValidator : AbstractValidator<UpdateEmergencyContactCommand>
{
    public UpdateEmergencyContactCommandValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Relationship).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(255).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
