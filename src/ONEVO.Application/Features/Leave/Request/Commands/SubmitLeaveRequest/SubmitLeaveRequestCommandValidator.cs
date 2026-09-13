using FluentValidation;
using ONEVO.Application.Features.Leave.Request.Helpers;

namespace ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;

public sealed class SubmitLeaveRequestCommandValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    public SubmitLeaveRequestCommandValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.EndAt)
            .GreaterThan(x => x.StartAt)
            .WithMessage(LeaveRequestMessages.EndAtNotAfterStartAt);
        RuleFor(x => x.EmployeeId).NotEmpty().When(x => x.IsOnBehalfRequest);
        RuleFor(x => x.Reason).MaximumLength(2000);
    }
}
