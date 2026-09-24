using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Monitoring.DeviceChangeRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.Monitoring;

/// <summary>Web/admin-facing endpoints for reviewing an employee's device-change request.
/// Requests are never submitted through this controller - they're raised automatically
/// by TrayEnrollmentService when a mismatch is detected. See the device-change-approval
/// design spec.</summary>
[ApiController]
[Route("api/v1/monitoring/device-change-requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class DeviceChangeRequestsController(IMediator mediator) : ControllerBase
{
    [HttpGet("approvals")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approvals([FromQuery] PagedRequest paging, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListDeviceChangeRequestApprovalsQuery(paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ReviewDeviceChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveDeviceChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] ReviewDeviceChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectDeviceChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
