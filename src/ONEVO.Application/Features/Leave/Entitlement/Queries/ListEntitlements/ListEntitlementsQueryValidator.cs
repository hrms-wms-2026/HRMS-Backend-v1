using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.ListEntitlements;

public class ListEntitlementsQueryValidator : AbstractValidator<ListEntitlementsQuery>
{
    public ListEntitlementsQueryValidator(IOptions<LeaveEntitlementYearOptions> options)
    {
        RuleFor(x => x.Year).MustBeConfiguredLeaveYear(options.Value);
    }
}
