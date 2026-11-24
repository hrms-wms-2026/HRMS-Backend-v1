using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitRejections;

/// <summary>The tenants with the most 429s on this API server in the last 1 to 60 minutes (task 49).</summary>
public sealed record GetRateLimitRejectionsQuery(int Minutes = IRateLimitRejectionStats.MaxWindowMinutes)
    : IRequest<Result<RateLimitRejectionsDto>>
{
    public const int MaxTenants = 50;
}
