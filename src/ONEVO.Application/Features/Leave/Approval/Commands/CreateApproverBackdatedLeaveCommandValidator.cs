using FluentValidation;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

/// <summary>Task 19. leave_requests.reason is nvarchar(max); 2000 matches SubmitLeaveRequestCommandValidator. The
/// handler checks that the day is in the past; this bounds how far back an approver may turn a day into leave.</summary>
public sealed class CreateApproverBackdatedLeaveCommandValidator : AbstractValidator<CreateApproverBackdatedLeaveCommand>
{
    public const int ReasonMaxLength = 2000;
    public const int MaxDaysBack = 366;

    public CreateApproverBackdatedLeaveCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.Reason).ValidName(ReasonMaxLength, required: false);
        RuleFor(x => x.WorkDate)
            .Must(date => date.DayNumber >= clock.Today.DayNumber - MaxDaysBack)
            .WithMessage($"Only a day in the last {MaxDaysBack} days can be turned into leave.");
    }
}
