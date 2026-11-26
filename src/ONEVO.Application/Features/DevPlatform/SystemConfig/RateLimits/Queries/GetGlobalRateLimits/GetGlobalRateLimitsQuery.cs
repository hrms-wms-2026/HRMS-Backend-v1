using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetGlobalRateLimits;

/// <summary>All six limits with the global override, the appsettings value, the effective value and the bounds.</summary>
public sealed record GetGlobalRateLimitsQuery : IRequest<Result<IReadOnlyList<GlobalRateLimitDto>>>;

public sealed class GetGlobalRateLimitsQueryHandler
    : IRequestHandler<GetGlobalRateLimitsQuery, Result<IReadOnlyList<GlobalRateLimitDto>>>
{
    private readonly RateLimitAdminService _service;

    public GetGlobalRateLimitsQueryHandler(RateLimitAdminService service) => _service = service;

    public async Task<Result<IReadOnlyList<GlobalRateLimitDto>>> Handle(GetGlobalRateLimitsQuery request, CancellationToken ct)
        => Result<IReadOnlyList<GlobalRateLimitDto>>.Success(await _service.GetGlobalAsync(ct));
}
