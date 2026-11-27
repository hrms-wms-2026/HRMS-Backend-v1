using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitChanges;

/// <summary>
/// Rate limit change history, newest first. Task 04's shared pagination contract is not built yet,
/// so this uses page (from 1) and pageSize (default 25, maximum 50), as task 47 says.
/// </summary>
public sealed record GetRateLimitChangesQuery(
    string? Scope,
    Guid? TenantId,
    Guid? SubscriptionPlanId,
    int Page = 1,
    int PageSize = GetRateLimitChangesQuery.DefaultPageSize) : IRequest<Result<PagedResult<RateLimitChangeDto>>>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 50;
}
