using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.CreateManualEntitlement;

public class CreateManualEntitlementCommandValidator : AbstractValidator<CreateManualEntitlementCommand>
{
    public CreateManualEntitlementCommandValidator(IOptions<LeaveEntitlementYearOptions> options)
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.Year).MustBeConfiguredLeaveYear(options.Value);
        RuleFor(x => x.TotalHours).GreaterThan(0);
        RuleFor(x => x.CarriedForwardHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
