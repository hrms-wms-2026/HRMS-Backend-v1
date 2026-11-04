using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;

public record ArchiveSubscriptionPlanCommand(Guid PlanId) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "subscription_plan.archived",
        TargetType = "subscription_plan",
        TargetId = PlanId.ToString(),
    };
}
