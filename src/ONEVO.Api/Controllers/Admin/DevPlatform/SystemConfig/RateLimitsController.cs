using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveGlobalRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetGlobalRateLimits;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitChanges;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// Platform-wide API rate limits (Improvements task 47). A rate limit is a platform safety setting,
/// so reads need system_config.read and writes system_config.manage. RateLimiting:Enabled is not
/// readable or writable here: the master switch stays in appsettings.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/system-config/rate-limits")]
public sealed class RateLimitsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RateLimitsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetGlobalRateLimitsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("changes")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> Changes(
        [FromQuery] string? scope,
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? planId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetRateLimitChangesQuery.DefaultPageSize,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetRateLimitChangesQuery(scope, tenantId, planId, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{limitName}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Set(string limitName, [FromBody] SetRateLimitRequest request, CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var result = await _mediator.Send(new SetGlobalRateLimitCommand(
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
        string limitName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemoveRateLimitRequest? body,
        [FromQuery] string? reason,
        [FromQuery] string? rowVersion,
        CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var (why, version) = RateLimitControllerSupport.RemoveArguments(body, reason, rowVersion);
        var result = await _mediator.Send(new RemoveGlobalRateLimitCommand(
            name, why, version, RateLimitControllerSupport.CorrelationId(HttpContext)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
