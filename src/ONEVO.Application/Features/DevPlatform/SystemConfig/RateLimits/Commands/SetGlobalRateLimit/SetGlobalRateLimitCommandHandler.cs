using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;

public sealed class SetGlobalRateLimitCommandHandler : IRequestHandler<SetGlobalRateLimitCommand, Result<IReadOnlyList<GlobalRateLimitDto>>>
{
    private readonly RateLimitAdminService _service;

    public SetGlobalRateLimitCommandHandler(RateLimitAdminService service) => _service = service;

    public async Task<Result<IReadOnlyList<GlobalRateLimitDto>>> Handle(SetGlobalRateLimitCommand request, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(
            new RateLimitChangeRequest(
                RateLimitTarget.Global,
                request.LimitName,
                new RateLimitValue(request.PerMinute, request.Burst),
                request.Reason,
                request.RowVersion,
                request.CorrelationId),
            ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<GlobalRateLimitDto>>.Failure(result.Error!, result.StatusCode ?? 400, result.ErrorCode);

        return Result<IReadOnlyList<GlobalRateLimitDto>>.Success(await _service.GetGlobalAsync(ct));
    }
}
