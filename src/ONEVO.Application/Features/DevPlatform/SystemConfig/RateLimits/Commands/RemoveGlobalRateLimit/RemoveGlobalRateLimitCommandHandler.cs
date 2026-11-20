using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveGlobalRateLimit;

public sealed class RemoveGlobalRateLimitCommandHandler : IRequestHandler<RemoveGlobalRateLimitCommand, Result<IReadOnlyList<GlobalRateLimitDto>>>
{
    private readonly RateLimitAdminService _service;

    public RemoveGlobalRateLimitCommandHandler(RateLimitAdminService service) => _service = service;

    public async Task<Result<IReadOnlyList<GlobalRateLimitDto>>> Handle(RemoveGlobalRateLimitCommand request, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(
            new RateLimitChangeRequest(
                RateLimitTarget.Global,
                request.LimitName,
                null,
                request.Reason,
                request.RowVersion,
                request.CorrelationId),
            ct);
        if (!result.IsSuccess)
            return Result<IReadOnlyList<GlobalRateLimitDto>>.Failure(result.Error!, result.StatusCode ?? 400, result.ErrorCode);

        return Result<IReadOnlyList<GlobalRateLimitDto>>.Success(await _service.GetGlobalAsync(ct));
    }
}
