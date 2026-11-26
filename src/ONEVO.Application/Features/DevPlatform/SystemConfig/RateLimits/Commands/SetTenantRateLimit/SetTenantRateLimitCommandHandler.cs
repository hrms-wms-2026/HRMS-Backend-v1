using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetTenantRateLimit;

public sealed class SetTenantRateLimitCommandHandler : IRequestHandler<SetTenantRateLimitCommand, Result<TenantRateLimitsDto>>
{
    private readonly RateLimitAdminService _service;

    public SetTenantRateLimitCommandHandler(RateLimitAdminService service) => _service = service;

    public async Task<Result<TenantRateLimitsDto>> Handle(SetTenantRateLimitCommand request, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(
            new RateLimitChangeRequest(
                RateLimitTarget.Tenant(request.TenantId),
                request.LimitName,
                new RateLimitValue(request.PerMinute, request.Burst),
                request.Reason,
                request.RowVersion,
                request.CorrelationId),
            ct);
        if (!result.IsSuccess)
            return Result<TenantRateLimitsDto>.Failure(result.Error!, result.StatusCode ?? 400, result.ErrorCode);

        return await _service.GetTenantAsync(request.TenantId, ct);
    }
}
