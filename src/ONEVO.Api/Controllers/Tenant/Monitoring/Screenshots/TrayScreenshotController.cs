using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetPendingCommands;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
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
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;

    public TrayScreenshotController(
        IMediator mediator,
        IFileStorageService fileStorage,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher)
    {
        _mediator = mediator;
        _fileStorage = fileStorage;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
    }

    /// <summary>
    /// Tray requests hit the base host (system mode), not a tenant subdomain, so no middleware
    /// has set PostgreSQL RLS tenant context yet. Every action here that touches tenant-owned
    /// data must call this first — see IngestActivitySnapshotsCommandHandler for the same pattern
    /// used by MediatR-based Tray handlers.
    /// </summary>
    private async Task<IActionResult?> SwitchToDeviceTenantAsync(CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty)
            return Problem("A valid tray device token is required.", statusCode: 401);

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Problem("Tenant not found.", statusCode: 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
        return null;
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

        if (await SwitchToDeviceTenantAsync(ct) is { } tenantError)
            return tenantError;

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

    /// <summary>
    /// Records one screenshot captured autonomously by the tray's periodic collector
    /// (no admin-issued command behind it — distinct from the poll/complete flow above).
    /// Maximum file size: 10 MB. Allowed types: PNG, JPEG, WebP.
    /// </summary>
    /// <response code="200">Screenshot stored and recorded.</response>
    [HttpPost("screenshots")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitPeriodicScreenshot(
        IFormFile file,
        [FromForm] DateTimeOffset capturedAt,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem("File is required.", statusCode: 400);

        var result = await _mediator.Send(
            new SubmitPeriodicScreenshotCommand(
                file.FileName, file.ContentType, file.OpenReadStream(), capturedAt),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { id = result.Value });
    }

    /// <summary>
    /// Records the outcome of one five-minute inactivity Allow/Skip prompt from
    /// InactivityScreenshotCollector — with a JPEG only when the outcome is "captured".
    /// Field names must match ONEVO.Agent.Service.Api.InactivityAttemptFormFields exactly.
    /// </summary>
    /// <response code="200">Attempt recorded (or already captured — a stable "captured" state).</response>
    /// <response code="400">Validation failed (see detail): bad outcome, missing/extra file, idle too short.</response>
    /// <response code="403">Policy no longer allows screenshot capture for this employee.</response>
    /// <response code="409">This attempt id was already recorded (idempotent retry — safe to stop retrying).</response>
    [HttpPost("inactivity-attempts")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitInactivityAttempt(
        [FromForm(Name = "attemptId")] Guid attemptId,
        [FromForm(Name = "policyVersion")] string policyVersion,
        [FromForm(Name = "idleStartedAt")] DateTimeOffset idleStartedAt,
        [FromForm(Name = "promptedAt")] DateTimeOffset promptedAt,
        [FromForm(Name = "decisionAt")] DateTimeOffset? decisionAt,
        [FromForm(Name = "capturedAt")] DateTimeOffset? capturedAt,
        [FromForm(Name = "idleDurationSeconds")] int idleDurationSeconds,
        [FromForm(Name = "monitorCount")] int monitorCount,
        [FromForm(Name = "outcome")] string outcome,
        [FromForm(Name = "failureCode")] string? failureCode,
        [FromForm(Name = "contentType")] string? contentType,
        [FromForm(Name = "sha256")] string? sha256,
        IFormFile? file,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SubmitInactivityCaptureAttemptCommand(
                attemptId,
                policyVersion,
                idleStartedAt,
                promptedAt,
                decisionAt,
                capturedAt,
                idleDurationSeconds,
                monitorCount,
                outcome,
                failureCode,
                contentType,
                sha256,
                file?.OpenReadStream()),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { id = result.Value });
    }
}
