using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;

public record ListSubscriptionPlansQuery : IRequest<Result<IReadOnlyList<SubscriptionPlanSummaryDto>>>;
