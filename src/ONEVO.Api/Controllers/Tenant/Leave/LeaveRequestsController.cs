using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Requests;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;
using ONEVO.Application.Features.Leave.Request.Queries.ListMyLeaveRequests;
using ONEVO.Application.Features.Leave.Request.Queries.PreviewSubmitLeaveRequest;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> Submit([FromBody] SubmitLeaveRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitLeaveRequestCommand(
            null,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.HalfDayPeriod,
            request.Reason,
            request.FileRecordIds ?? [],
            false), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("preview")]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> Preview([FromBody] SubmitLeaveRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new PreviewSubmitLeaveRequestQuery(
            null,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.HalfDayPeriod,
            request.Reason,
            request.FileRecordIds ?? [],
            false), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("on-behalf")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> SubmitOnBehalf([FromBody] SubmitLeaveRequestOnBehalfRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitLeaveRequestCommand(
            request.EmployeeId,
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.HalfDayPeriod,
            request.Reason,
            request.FileRecordIds ?? [],
            true), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("my")]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> ListMine(
        [FromQuery] string? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? leaveTypeId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMyLeaveRequestsQuery(status, fromDate, toDate, leaveTypeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
