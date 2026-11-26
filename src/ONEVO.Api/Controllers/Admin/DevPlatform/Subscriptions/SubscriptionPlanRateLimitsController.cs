using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemovePlanRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetPlanRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetPlanRateLimits;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;

/// <summary>
/// Subscription plan overrides of SignedIn, TrayDevice and TenantCeiling (Improvements task 47). A rate limit is a
/// platform safety setting, not a commercial term, so these routes need system_config.read and
/// system_config.manage like the global ones.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/subscription-plans/{id:guid}/rate-limits")]
public sealed class SubscriptionPlanRateLimitsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionPlanRateLimitsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPlanRateLimitsQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{limitName}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Set(
        Guid id, string limitName, [FromBody] SetRateLimitRequest request, CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var result = await _mediator.Send(new SetPlanRateLimitCommand(
            id,
            name,
            request.PerMinute,
            request.Burst,
            request.Reason ?? string.Empty,
            request.RowVersion,
            RateLimitControllerSupport.CorrelationId(HttpContext)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{limitName}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Remove(
        Guid id,
        string limitName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemoveRateLimitRequest? body,
        [FromQuery] string? reason,
        [FromQuery] string? rowVersion,
        CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var (why, version) = RateLimitControllerSupport.RemoveArguments(body, reason, rowVersion);
        var result = await _mediator.Send(new RemovePlanRateLimitCommand(
            id, name, why, version, RateLimitControllerSupport.CorrelationId(HttpContext)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
