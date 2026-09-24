using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;

public class UpdatePersonalInformationCommandValidator : AbstractValidator<UpdatePersonalInformationCommand>
{
    public UpdatePersonalInformationCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Version).NotEmpty().WithMessage("A concurrency version token is required.");
        RuleForEach(x => x.Addresses).ChildRules(address =>
        {
            address.RuleFor(a => a.AddressType).Must(t => t is "permanent" or "current")
                .WithMessage("Address type must be 'permanent' or 'current'.");
        });
    }
}
