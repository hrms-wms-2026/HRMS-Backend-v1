using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;

public class ListAllBalancesQueryValidator : AbstractValidator<ListAllBalancesQuery>
{
    public ListAllBalancesQueryValidator(IOptions<LeaveEntitlementYearOptions> options)
    {
        RuleFor(x => x.Year).MustBeConfiguredLeaveYear(options.Value);
    }
}
