using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;

public record GetSubscriptionPlanQuery(Guid PlanId, bool IncludeRateLimits = false) : IRequest<Result<SubscriptionPlanDetailDto>>;
