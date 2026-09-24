using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.RequestScreenshot;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshotUrl;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Screenshots;

/// <summary>
/// HR/admin APIs for requesting on-demand screenshots and viewing evidence assets.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/screenshots")]
[Authorize(Policy = "TenantPolicy")]
public class ScreenshotController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScreenshotController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Send an on-demand screenshot capture command to a specific agent device.
    /// The device must be active and screenshot capture must be enabled for that employee.
    /// </summary>
    /// <response code="201">Command created and queued for the agent to pick up.</response>
    /// <response code="403">Screenshot capture is disabled for this employee.</response>
    /// <response code="404">Agent device not found or does not belong to this tenant.</response>
    /// <response code="409">Agent device is not currently active.</response>
    [HttpPost("request")]
    [RequirePermission("agent:command")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestScreenshot(
        [FromBody] RequestScreenshotRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RequestScreenshotCommand(request.AgentDeviceId, request.Note), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// List screenshot evidence assets. Does not return file URLs — use GET /{id}/url for access.
    /// </summary>
    /// <response code="200">Paginated list of evidence assets.</response>
    [HttpGet]
    [RequirePermission("monitoring:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListScreenshots(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetScreenshotsQuery(employeeId, from, to, page, pageSize), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a short-lived signed URL (15 minutes) to view a specific screenshot.
    /// Do not cache or store this URL — call again when needed.
    /// </summary>
    /// <response code="200">Signed URL and its expiry time.</response>
    /// <response code="404">Evidence asset not found.</response>
    [HttpGet("{id:guid}/url")]
    [RequirePermission("monitoring:read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScreenshotUrl(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetScreenshotUrlQuery(id), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
