using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;

public sealed class ResendEmployeeInvitationCommandValidator : AbstractValidator<ResendEmployeeInvitationCommand>
{
    public ResendEmployeeInvitationCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}
