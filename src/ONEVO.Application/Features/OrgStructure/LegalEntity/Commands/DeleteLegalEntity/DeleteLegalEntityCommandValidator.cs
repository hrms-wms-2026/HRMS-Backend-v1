using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;

public class DeleteLegalEntityCommandValidator : AbstractValidator<DeleteLegalEntityCommand>
{
    public DeleteLegalEntityCommandValidator()
    {
        RuleFor(x => x.ConfirmName)
            .NotEmpty().WithMessage("Company name confirmation is required.");
    }
}
