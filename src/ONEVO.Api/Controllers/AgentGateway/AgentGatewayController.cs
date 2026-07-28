using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;
using ONEVO.Application.Features.AgentGateway.Commands.CaptureSetupLocation;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteScreenshotCommand;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.RespondAgentCommand;
using ONEVO.Application.Features.AgentGateway.Commands.RecordMonitoringConsent;
using ONEVO.Application.Features.AgentGateway.Commands.RecordConsentEvent;
using ONEVO.Application.Features.AgentGateway.Commands.UpdateHeartbeat;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;
using ONEVO.Application.Features.AgentGateway.Queries.GetPendingAgentCommands;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentSetupStatus;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentWorkContext;
using ONEVO.Application.Features.AgentGateway.Queries.GetDeviceChangeStatus;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.IdentityVerification.Commands.EnrollReferencePhoto;
using ONEVO.Application.Features.IdentityVerification.Commands.VerifyFace;

namespace ONEVO.Api.Controllers.AgentGateway;

[ApiController]
[Route("api/v1/agent")]
public class AgentGatewayController : ControllerBase
{
    private readonly IMediator _mediator;

    public AgentGatewayController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── Enrollment ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 1: TrayApp starts enrollment.
    /// Anonymous — device has no credential yet.
    /// </summary>
    [HttpPost("enroll/start")]
    [AllowAnonymous]
    public async Task<IActionResult> EnrollStart([FromBody] EnrollStartRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new StartEnrollmentCommand(request.DeviceId, request.DeviceName, request.OsVersion, request.AgentVersion, request.RedirectUri), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new
        {
            enrollment_id = result.Value!.EnrollmentId,
            auth_url = result.Value.AuthUrl,
            expires_at = result.Value.ExpiresAt
        });
    }

    /// <summary>
    /// Step 2: Authenticated employee confirms device in the browser.
    /// Uses TenantPolicy (web cookie session).
    /// Frontend returns authorization_code to TrayApp via callback URI.
    /// </summary>
    [HttpPost("enroll/confirm")]
    [Authorize(Policy = "TenantPolicy")]
    public async Task<IActionResult> EnrollConfirm([FromBody] EnrollConfirmRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmEnrollmentCommand(request.EnrollmentId), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        if (!string.IsNullOrEmpty(result.Value!.RedirectUri))
        {
            if (!Uri.TryCreate(result.Value.RedirectUri, UriKind.Absolute, out var redirectUri)
                || redirectUri.Scheme != "http"
                || !redirectUri.IsLoopback)
            {
                return Problem("redirect_uri is not a valid loopback address.", statusCode: 400);
            }
            var separator = result.Value.RedirectUri.Contains('?') ? '&' : '?';
            return Redirect($"{result.Value.RedirectUri}{separator}code={Uri.EscapeDataString(result.Value.AuthorizationCode)}");
        }

        return Ok(new { authorization_code = result.Value.AuthorizationCode });
    }

    /// <summary>
    /// Step 3: TrayApp completes enrollment with the authorization_code.
    /// Anonymous — device doesn't have a credential yet.
    /// Returns 201 with device_token + policy.
    /// </summary>
    [HttpPost("enroll/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> EnrollComplete([FromBody] EnrollCompleteRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CompleteEnrollmentCommand(request.EnrollmentId, request.DeviceId, request.AuthorizationCode), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var dto = result.Value!;
        return StatusCode(201, new
        {
            agent_id = dto.AgentId,
            tenant_id = dto.TenantId,
            employee_id = dto.EmployeeId,
            employee_name = dto.EmployeeName,
            device_token = dto.DeviceToken,
            token_expires_at = dto.TokenExpiresAt,
            policy = dto.PolicyJson,
            device_approval_status = dto.DeviceApprovalStatus,
            device_change_request_id = dto.DeviceChangeRequestId
        });
    }

    // ── Agent session (Device JWT required) ────────────────────────────────────

    /// <summary>
    /// Resume/refresh employee-device session on an enrolled agent.
    /// </summary>
    [HttpPost("login")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(new AgentLoginCommand(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new
        {
            employee_id = result.Value!.EmployeeId,
            employee_name = result.Value.EmployeeName,
            policy = result.Value.PolicyJson
        });
    }

    /// <summary>
    /// End active employee-device session.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(new AgentLogoutCommand(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok();
    }

    /// <summary>
    /// Agent heartbeat every 60 s. Persists health snapshot and touches last_heartbeat_at.
    /// </summary>
    [HttpPost("heartbeat")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(
            new UpdateHeartbeatCommand(agentId, tenantId, (decimal)request.CpuUsage, request.MemoryMb, request.MonitoringState), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var pendingCommands = await _mediator.Send(
            new GetPendingAgentCommandsQuery(agentId, 20),
            ct);
        var pendingCount = pendingCommands.IsSuccess
            ? pendingCommands.Value!.Count
            : 0;
        return Ok(new
        {
            status = "ok",
            update_available = false,
            update_url = (string?)null,
            has_pending_commands = pendingCount > 0,
            pending_command_count = pendingCount
        });
    }

    /// <summary>
    /// Returns the monitoring policy for the calling agent.
    /// </summary>
    [HttpGet("policy")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(new GetAgentPolicyQuery(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new
        {
            agent_id = agentId,
            policy = result.Value,
            // Temporary compatibility field for an older tray build that
            // expects the effective policy as an encoded JSON string.
            policy_json = JsonSerializer.Serialize(result.Value)
        });
    }

    /// <summary>
    /// Authoritative server-side work context: schedule, monitoring state, effective policy.
    /// Agent reconciles local state against this on startup and after reconnect.
    /// </summary>
    [HttpGet("work-context")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetWorkContext(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(new GetAgentWorkContextQuery(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    [HttpPost("monitoring-consent")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> RecordMonitoringConsent(
        [FromBody] MonitoringConsentRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new RecordMonitoringConsentCommand(
                agentId,
                request.Consented,
                request.NoticeVersion),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Records a per-incident screenshot consent decision (allowed / denied / timeout /
    /// upload_failed_no_image). Idempotent — first terminal decision wins.
    /// </summary>
    [HttpPost("consent-events")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> RecordConsentEvent(
        [FromBody] ConsentEventRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var result = await _mediator.Send(
            new RecordConsentEventCommand(tenantId, agentId, request.IncidentId, request.Decision, request.OccurredAt),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok();
    }

    /// <summary>
    /// Returns only the signed-in device's setup readiness. Employee, tenant,
    /// and agent authority are resolved from the device JWT and database.
    /// </summary>
    [HttpGet("setup/status")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetSetupStatus(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(new GetAgentSetupStatusQuery(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var status = result.Value!;
        return Ok(new
        {
            work_mode = status.WorkMode,
            location_required = status.LocationRequired,
            location_ready = status.LocationReady,
            reference_required = status.ReferenceRequired,
            reference_ready = status.ReferenceReady,
            remote_profile_status = status.RemoteProfileStatus,
            setup_state = status.SetupState
        });
    }

    /// <summary>
    /// Captures an explicit OS-permission location sample for setup. Public IP
    /// comes from trusted request metadata and is never accepted in this body.
    /// </summary>
    [HttpPost("setup/location")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> CaptureSetupLocation(
        [FromBody] CaptureSetupLocationRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(new CaptureSetupLocationCommand(
            agentId,
            new LocationCapture(
                request.Latitude,
                request.Longitude,
                request.AccuracyMeters,
                request.CapturedAt,
                request.PermissionState),
            request.LocalNetworkClass,
            request.WifiBssidHash,
            request.GatewayMacHash,
            request.VpnDetected), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var capture = result.Value!;
        return Ok(new
        {
            evidence_id = capture.EvidenceId,
            match_state = capture.MatchState,
            remote_profile_state = capture.RemoteProfileState,
            failure_code = capture.FailureCode,
            distance_meters = capture.DistanceMeters
        });
    }

    [HttpPost("setup/reference-photo")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> EnrollReferencePhoto(
        [FromForm] IFormFile photo,
        [FromForm] string noticeVersion,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();
        if (photo is null || photo.Length <= 0)
            return BadRequest(new { error = "Reference photo is required." });

        await using var stream = photo.OpenReadStream();
        var result = await _mediator.Send(
            new EnrollReferencePhotoCommand(
                agentId,
                noticeVersion,
                photo.FileName,
                photo.ContentType,
                stream),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    [HttpPost("verification/face")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> VerifyFace(
        [FromForm] IFormFile photo,
        [FromForm] string trigger,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();
        if (photo is null || photo.Length <= 0)
            return BadRequest(new { error = "Verification photo is required." });

        await using var stream = photo.OpenReadStream();
        var result = await _mediator.Send(
            new VerifyFaceCommand(
                agentId,
                trigger,
                photo.FileName,
                photo.ContentType,
                stream),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Accepts a batch of activity events from the agent. Stored raw for async processing.
    /// Returns 202 immediately.
    /// </summary>
    [HttpPost("ingest")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> Ingest([FromBody] IngestBatchRequest request, CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty) return Unauthorized();

        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var payloadJson = JsonSerializer.Serialize(request,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var result = await _mediator.Send(new IngestBatchCommand(agentId, tenantId, payloadJson), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }

    [HttpGet("commands")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> GetCommands(
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(
            new GetPendingAgentCommandsQuery(agentId, limit),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { commands = result.Value });
    }

    [HttpPost("commands/{id:guid}/response")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    public async Task<IActionResult> RespondToCommand(
        Guid id,
        [FromBody] AgentCommandResponseRequest request,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(new RespondAgentCommand(
            agentId,
            id,
            request.Decision,
            request.ConsentNoticeVersion), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    [HttpPost("commands/{id:guid}/screenshot")]
    [Authorize(Policy = "ActiveAgentPolicy")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> CompleteScreenshot(
        Guid id,
        [FromForm] IFormFile screenshot,
        CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();
        if (screenshot is null || screenshot.Length <= 0)
            return BadRequest(new { error = "Screenshot file is required." });

        await using var stream = screenshot.OpenReadStream();
        var result = await _mediator.Send(new CompleteScreenshotCommand(
            agentId,
            id,
            screenshot.FileName,
            screenshot.ContentType,
            stream), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>
    /// Candidate-safe approval status polling. The calling agent id is always
    /// resolved from its signed JWT and is never accepted from request input.
    /// </summary>
    [HttpGet("device-change/status")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> GetDeviceChangeStatus(CancellationToken ct)
    {
        var agentId = GetAgentId();
        if (agentId == Guid.Empty)
            return Unauthorized();

        var result = await _mediator.Send(new GetDeviceChangeStatusQuery(agentId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Guid GetAgentId()
    {
        // MapInboundClaims = false on AgentScheme — "sub" is not remapped to NameIdentifier
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private Guid GetTenantId()
    {
        var value = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    // ── Request shapes ─────────────────────────────────────────────────────────

    public record EnrollStartRequest(
        string DeviceId,
        string DeviceName,
        string OsVersion,
        string AgentVersion,
        string? RedirectUri = null);

    public record EnrollConfirmRequest(Guid EnrollmentId);

    public record EnrollCompleteRequest(
        Guid EnrollmentId,
        string DeviceId,
        string AuthorizationCode);

    public record HeartbeatRequest(
        string DeviceId,
        string AgentVersion,
        double CpuUsage,
        int MemoryMb,
        int BufferCount,
        string MonitoringState);

    public sealed record CaptureSetupLocationRequest(
        decimal Latitude,
        decimal Longitude,
        decimal AccuracyMeters,
        DateTimeOffset CapturedAt,
        string PermissionState,
        string? LocalNetworkClass,
        string? WifiBssidHash,
        string? GatewayMacHash,
        bool VpnDetected);

    public record IngestBatchRequest(
        DateTimeOffset Timestamp,
        JsonElement[] Batch);

    public sealed record AgentCommandResponseRequest(
        string Decision,
        string ConsentNoticeVersion);

    public sealed record MonitoringConsentRequest(
        bool Consented,
        string NoticeVersion);

    public sealed record ConsentEventRequest(
        Guid IncidentId,
        string Decision,
        DateTimeOffset OccurredAt);
}
