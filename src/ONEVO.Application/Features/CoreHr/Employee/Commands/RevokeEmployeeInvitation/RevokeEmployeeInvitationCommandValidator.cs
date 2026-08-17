using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;

public sealed class RevokeEmployeeInvitationCommandValidator : AbstractValidator<RevokeEmployeeInvitationCommand>
{
    public RevokeEmployeeInvitationCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
