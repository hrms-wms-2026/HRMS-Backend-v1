using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;

public class PreviewGenerateEntitlementsQueryValidator : AbstractValidator<PreviewGenerateEntitlementsQuery>
{
    public PreviewGenerateEntitlementsQueryValidator(IOptions<LeaveEntitlementYearOptions> options)
    {
        RuleFor(x => x.Year).MustBeConfiguredLeaveYear(options.Value);
    }
}
