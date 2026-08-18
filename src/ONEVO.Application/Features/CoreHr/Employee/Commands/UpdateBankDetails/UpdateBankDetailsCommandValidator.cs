using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public class UpdateBankDetailsCommandValidator : AbstractValidator<UpdateBankDetailsCommand>
{
    public UpdateBankDetailsCommandValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BranchName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountHolderName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(34);
        RuleFor(x => x.AccountType).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RoutingNumber).MaximumLength(20);
    }
}
