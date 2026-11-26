using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveTenantRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetTenantRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetTenantRateLimits;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

/// <summary>
/// Tenant overrides of SignedIn, TrayDevice and TenantCeiling (Improvements task 47). A rate limit is a
/// platform safety setting, not a commercial term, so these routes need system_config.read and
/// system_config.manage like the global ones.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/tenants/{tenantId:guid}/rate-limits")]
public sealed class AdminTenantRateLimitsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantRateLimitsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantRateLimitsQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{limitName}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Set(
        Guid tenantId, string limitName, [FromBody] SetRateLimitRequest request, CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var result = await _mediator.Send(new SetTenantRateLimitCommand(
            tenantId,
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
        Guid tenantId,
        string limitName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemoveRateLimitRequest? body,
        [FromQuery] string? reason,
        [FromQuery] string? rowVersion,
        CancellationToken ct)
    {
        if (!RateLimitNames.TryParse(limitName, out var name))
            return RateLimitControllerSupport.UnknownLimit(this, limitName);

        var (why, version) = RateLimitControllerSupport.RemoveArguments(body, reason, rowVersion);
        var result = await _mediator.Send(new RemoveTenantRateLimitCommand(
            tenantId, name, why, version, RateLimitControllerSupport.CorrelationId(HttpContext)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
