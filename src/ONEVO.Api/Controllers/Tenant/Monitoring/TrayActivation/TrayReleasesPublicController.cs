using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.TrayActivation;

/// <summary>
/// Anonymous, read-only installer metadata. Not secret: the file is public and integrity is enforced
/// client-side via sha256. Anonymous so the tray can check for updates before/without a session.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/tray/releases")]
public sealed class TrayReleasesPublicController : ControllerBase
{
    private readonly IMediator _mediator;
    public TrayReleasesPublicController(IMediator mediator) => _mediator = mediator;

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string channel = "stable", CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLatestTrayReleaseQuery(channel), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check(
        [FromQuery] string current, [FromQuery] string channel = "stable", CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CheckTrayUpdateQuery(current, channel), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
