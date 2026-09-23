using FluentValidation;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public static class LeaveEntitlementYearRules
{
    public static IRuleBuilderOptions<T, int> MustBeConfiguredLeaveYear<T>(
        this IRuleBuilder<T, int> rule, LeaveEntitlementYearOptions options)
    {
        return rule.InclusiveBetween(options.MinimumYear, options.MaximumYear)
            .WithMessage(LeaveEntitlementMessages.YearUnavailable);
    }
}
