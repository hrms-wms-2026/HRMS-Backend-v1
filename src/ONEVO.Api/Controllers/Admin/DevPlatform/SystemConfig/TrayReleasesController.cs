using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ONEVO.Api.Configuration;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.ListTrayReleases;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>
/// Tray installer release registry (platform admin).
///   GET  /admin/v1/tray-releases          → list
///   POST /admin/v1/tray-releases          → create (admin)
///   PUT  /admin/v1/tray-releases/{id}     → update / promote / activate / deactivate
///   POST /admin/v1/tray-releases/ingest   → CI registration (X-Release-Token, always inactive)
/// </summary>
[ApiController]
public sealed class TrayReleasesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly TrayReleasesOptions _options;

    public TrayReleasesController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser,
        IOptions<TrayReleasesOptions> options)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _options = options.Value;
    }

    [HttpGet("admin/v1/tray-releases")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListTrayReleasesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/tray-releases")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Create([FromBody] CreateTrayReleaseRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null) return Forbid();

        var result = await _mediator.Send(
            new CreateTrayReleaseCommand(request, TrayReleaseSources.Admin, actorId), ct);
        return result.IsSuccess
            ? Created($"admin/v1/tray-releases/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("admin/v1/tray-releases/{id:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateTrayReleaseRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateTrayReleaseCommand(id, request), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Called by the tray release pipeline. Never activates a build.</summary>
    [HttpPost("admin/v1/tray-releases/ingest")]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest([FromBody] CreateTrayReleaseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.IngestToken))
            return NotFound(); // feature off unless a token is configured

        var presented = Request.Headers["X-Release-Token"].ToString();
        if (!IngestTokenValidator.IsValid(_options.IngestToken, presented))
            return Unauthorized();

        var safe = request with { IsActive = false };
        var result = await _mediator.Send(
            new CreateTrayReleaseCommand(safe, TrayReleaseSources.Ci, null), ct);
        return result.IsSuccess
            ? Created($"admin/v1/tray-releases/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
