using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogin;
using ONEVO.Application.Features.AgentGateway.Commands.AgentLogout;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;
using ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;
using ONEVO.Application.Features.AgentGateway.Commands.UpdateHeartbeat;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;

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
    [Authorize(Policy = "AgentPolicy")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var deviceId = User.FindFirst("sub")?.Value ?? string.Empty;
        var result = await _mediator.Send(new AgentLogoutCommand(deviceId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok();
    }

    /// <summary>
    /// Agent heartbeat every 60 s. Persists health snapshot and touches last_heartbeat_at.
    /// </summary>
    [HttpPost("heartbeat")]
    [Authorize(Policy = "AgentPolicy")]
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

        return Ok(new
        {
            status = "ok",
            update_available = false,
            update_url = (string?)null,
            has_pending_commands = false,
            pending_command_count = 0
        });
    }

    /// <summary>
    /// Returns the monitoring policy for the calling agent.
    /// </summary>
    [HttpGet("policy")]
    [Authorize(Policy = "AgentPolicy")]
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
            policy_json = result.Value
        });
    }

    /// <summary>
    /// Accepts a batch of activity events from the agent. Stored raw for async processing.
    /// Returns 202 immediately.
    /// </summary>
    [HttpPost("ingest")]
    [Authorize(Policy = "AgentPolicy")]
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

    public record IngestBatchRequest(
        Guid DeviceId,
        Guid EmployeeId,
        DateTimeOffset Timestamp,
        JsonElement[] Batch);
}
