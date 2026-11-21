using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;

public record ArchiveSubscriptionPlanCommand(Guid PlanId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("subscription_plan.archived", "subscription_plan", PlanId);
}
