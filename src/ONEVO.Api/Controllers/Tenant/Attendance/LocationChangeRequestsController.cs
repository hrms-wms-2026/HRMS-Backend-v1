using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.LocationChangeRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

/// <summary>Web/admin-facing endpoints for reviewing an employee's requested change to their
/// registered remote work location. Submitting a request and answering the post-clock-in
/// "save as new location?" prompt both happen from the tray app instead - see
/// TrayLocationChangeRequestsController.</summary>
[ApiController]
[Route("api/v1/attendance/location-change-requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LocationChangeRequestsController(IMediator mediator) : ControllerBase
{
    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromQuery] string? status, [FromQuery] PagedRequest paging, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyLocationChangeRequestsQuery(status, paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("approvals")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approvals([FromQuery] PagedRequest paging, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListLocationChangeRequestApprovalsQuery(paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ReviewLocationChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveLocationChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] ReviewLocationChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectLocationChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CancelLocationChangeRequestCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
