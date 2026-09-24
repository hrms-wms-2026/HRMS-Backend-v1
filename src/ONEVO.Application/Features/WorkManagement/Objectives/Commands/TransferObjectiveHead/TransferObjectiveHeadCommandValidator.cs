using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;

public class TransferObjectiveHeadCommandValidator : AbstractValidator<TransferObjectiveHeadCommand>
{
    public TransferObjectiveHeadCommandValidator()
    {
        RuleFor(x => x.NewHeadEmployeeId)
            .NotEqual(Guid.Empty).WithMessage("A new head employee id is required.");
    }
}
