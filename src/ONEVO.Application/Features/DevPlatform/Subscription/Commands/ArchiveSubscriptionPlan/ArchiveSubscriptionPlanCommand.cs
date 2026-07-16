using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;

public record ArchiveSubscriptionPlanCommand(Guid PlanId) : IRequest<Result>;
