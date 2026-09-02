using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.WorkAreaChangeRequests;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.Queries.WorkAreaChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

[ApiController]
[Route("api/v1/attendance/work-area-change-requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class WorkAreaChangeRequestsController(IMediator mediator) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromBody] WorkAreaChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new PreviewWorkAreaChangeRequestCommand(
            request.Date, request.RequestedWorkArea, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] WorkAreaChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateWorkAreaChangeRequestCommand(
            request.Date, request.RequestedWorkArea, request.Reason), ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyWorkAreaChangeRequestsQuery(from, to, status, paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("approvals")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approvals(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListWorkAreaChangeRequestApprovalsQuery(from, to, paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ReviewWorkAreaChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveWorkAreaChangeRequestCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] ReviewWorkAreaChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectWorkAreaChangeRequestCommand(id, request.ReviewComment ?? string.Empty), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CancelWorkAreaChangeRequestCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
