using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetPendingCommands;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Screenshots;

/// <summary>
/// Agent (tray) endpoints for polling commands and reporting results.
/// Authentication: Bearer tray_access_token (TrayDevicePolicy).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/tray")]
[Authorize(Policy = "TrayDevicePolicy")]
public class TrayScreenshotController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileStorageService _fileStorage;
    private readonly ITrayCurrentDevice _device;

    public TrayScreenshotController(
        IMediator mediator,
        IFileStorageService fileStorage,
        ITrayCurrentDevice device)
    {
        _mediator = mediator;
        _fileStorage = fileStorage;
        _device = device;
    }

    /// <summary>
    /// Poll for pending commands addressed to this device.
    /// Commands are marked as delivered upon this response — agent must complete or fail them.
    /// </summary>
    /// <response code="200">List of pending commands (may be empty).</response>
    [HttpGet("commands")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingCommands(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPendingCommandsQuery(), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Report the outcome of a command execution.
    /// Include FileRecordId when Success=true (obtained from POST /tray/upload).
    /// </summary>
    /// <response code="200">Command outcome recorded.</response>
    /// <response code="403">Command does not belong to this device.</response>
    /// <response code="404">Command not found.</response>
    /// <response code="409">Command is no longer pending.</response>
    /// <response code="410">Command has expired.</response>
    [HttpPost("commands/{id:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> CompleteCommand(
        Guid id,
        [FromBody] CompleteCommandRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompleteAgentCommandCommand(
                id,
                request.Success,
                request.ResultJson,
                request.FileRecordId,
                request.CapturedAt),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok();
    }

    /// <summary>
    /// Upload a screenshot binary. Returns a FileRecordId to include in POST /commands/{id}/complete.
    /// Maximum file size: 10 MB. Allowed types: PNG, JPEG, WebP.
    /// </summary>
    /// <response code="200">Upload successful, returns FileRecordDto with Id.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadScreenshot(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem("File is required.", statusCode: 400);

        var result = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            file.FileName,
            file.ContentType,
            UploadPurposeCatalog.MonitoringScreenshot,
            file.OpenReadStream(),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}
