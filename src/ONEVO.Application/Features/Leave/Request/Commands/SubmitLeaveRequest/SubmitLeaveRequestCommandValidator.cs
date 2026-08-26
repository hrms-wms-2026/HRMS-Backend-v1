using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;

public sealed class SubmitLeaveRequestCommandValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    private static readonly string[] HalfDayValues = [LeaveHalfDayPeriods.Am, LeaveHalfDayPeriods.Pm];

    public SubmitLeaveRequestCommandValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.HalfDayPeriod)
            .Must(value => value is null || HalfDayValues.Contains(value))
            .WithMessage("Half-day period must be am or pm.");
        RuleFor(x => x.EmployeeId).NotEmpty().When(x => x.IsOnBehalfRequest);
        RuleFor(x => x.Reason).MaximumLength(2000);
    }
}
