using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.ApproveDeviceChange;
using ONEVO.Application.Features.AgentGateway.Commands.RejectDeviceChange;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentHealthList;
using ONEVO.Application.Features.AgentGateway.Queries.GetPendingDeviceChanges;

namespace ONEVO.Api.Controllers.AgentGateway;

[ApiController]
[Route("api/v1/agents")]
[Authorize]
public class AgentFleetController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AgentFleetController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <summary>Fleet health list — all active agents for this tenant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFleet(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAgentHealthListQuery(), ct);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Pending employee device replacements for HR/Admin review.</summary>
    [HttpGet("device-change-requests")]
    [Authorize(Policy = "TenantPolicy")]
    [RequirePermission("agent:manage")]
    public async Task<IActionResult> GetPendingDeviceChanges(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetPendingDeviceChangesQuery(page, pageSize), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }

    /// <summary>Approve a replacement and atomically switch the employee's active device.</summary>
    [HttpPut("device-change-requests/{id:guid}/approve")]
    [Authorize(Policy = "TenantPolicy")]
    [RequirePermission("agent:manage")]
    public async Task<IActionResult> ApproveDeviceChange(
        Guid id,
        [FromBody] DeviceChangeReviewRequest? request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ApproveDeviceChangeCommand(id, request?.ReviewComment, _currentUser.UserId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { status = "approved" });
    }

    /// <summary>Reject a replacement while leaving the employee's approved device active.</summary>
    [HttpPut("device-change-requests/{id:guid}/reject")]
    [Authorize(Policy = "TenantPolicy")]
    [RequirePermission("agent:manage")]
    public async Task<IActionResult> RejectDeviceChange(
        Guid id,
        [FromBody] DeviceChangeReviewRequest? request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RejectDeviceChangeCommand(id, request?.ReviewComment, _currentUser.UserId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { status = "rejected" });
    }

    public sealed record DeviceChangeReviewRequest(string? ReviewComment);
}
