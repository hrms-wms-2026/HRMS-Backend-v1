using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandValidator : AbstractValidator<TransferObjectiveHeadCommand>
{
    public TransferObjectiveHeadCommandValidator()
    {
        RuleFor(x => x.NewHeadUserId)
            .NotEqual(Guid.Empty).WithMessage("A new head user id is required.");
    }
}
