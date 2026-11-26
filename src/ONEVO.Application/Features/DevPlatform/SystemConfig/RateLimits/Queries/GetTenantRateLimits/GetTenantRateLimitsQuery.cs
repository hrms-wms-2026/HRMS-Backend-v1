using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetTenantRateLimits;

/// <summary>A tenant's overrides and effective per-tenant limits, with where each value comes from.</summary>
public sealed record GetTenantRateLimitsQuery(Guid TenantId) : IRequest<Result<TenantRateLimitsDto>>;

public sealed class GetTenantRateLimitsQueryHandler : IRequestHandler<GetTenantRateLimitsQuery, Result<TenantRateLimitsDto>>
{
    private readonly RateLimitAdminService _service;

    public GetTenantRateLimitsQueryHandler(RateLimitAdminService service) => _service = service;

    public Task<Result<TenantRateLimitsDto>> Handle(GetTenantRateLimitsQuery request, CancellationToken ct)
        => _service.GetTenantAsync(request.TenantId, ct);
}
