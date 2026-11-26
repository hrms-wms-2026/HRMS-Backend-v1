using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetPlanRateLimit;

public sealed class SetPlanRateLimitCommandHandler : IRequestHandler<SetPlanRateLimitCommand, Result<PlanRateLimitsDto>>
{
    private readonly RateLimitAdminService _service;

    public SetPlanRateLimitCommandHandler(RateLimitAdminService service) => _service = service;

    public async Task<Result<PlanRateLimitsDto>> Handle(SetPlanRateLimitCommand request, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(
            new RateLimitChangeRequest(
                RateLimitTarget.Plan(request.SubscriptionPlanId),
                request.LimitName,
                new RateLimitValue(request.PerMinute, request.Burst),
                request.Reason,
                request.RowVersion,
                request.CorrelationId),
            ct);
        if (!result.IsSuccess)
            return Result<PlanRateLimitsDto>.Failure(result.Error!, result.StatusCode ?? 400, result.ErrorCode);

        return await _service.GetPlanAsync(request.SubscriptionPlanId, ct);
    }
}
