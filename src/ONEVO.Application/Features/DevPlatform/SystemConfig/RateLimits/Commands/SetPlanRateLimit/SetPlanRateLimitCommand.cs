using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetPlanRateLimit;

/// <summary>Improvements task 47. Needs platform.system_config.manage.</summary>
public sealed record SetPlanRateLimitCommand(
    Guid SubscriptionPlanId,
    RateLimitName LimitName,
    int PerMinute,
    int? Burst,
    string Reason,
    string? RowVersion,
    string? CorrelationId) : IRequest<Result<PlanRateLimitsDto>>, ISetRateLimitCommand;
