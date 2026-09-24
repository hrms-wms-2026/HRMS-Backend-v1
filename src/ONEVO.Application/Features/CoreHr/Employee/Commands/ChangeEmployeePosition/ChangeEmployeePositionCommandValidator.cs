using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed class ChangeEmployeePositionCommandValidator : AbstractValidator<ChangeEmployeePositionCommand>
{
    public ChangeEmployeePositionCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.EffectiveFrom).NotEmpty();
        RuleFor(x => x.ChangeReason).Must(r => r is "Promotion" or "Transfer" or "LateralMove");
    }
}
