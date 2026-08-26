using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Options;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;

public class GenerateEntitlementsCommandValidator : AbstractValidator<GenerateEntitlementsCommand>
{
    public GenerateEntitlementsCommandValidator(IOptions<LeaveEntitlementYearOptions> options)
    {
        RuleFor(x => x.Year).MustBeConfiguredLeaveYear(options.Value);
    }
}
