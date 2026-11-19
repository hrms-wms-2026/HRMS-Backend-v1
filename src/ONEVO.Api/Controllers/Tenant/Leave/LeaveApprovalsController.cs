using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Approvals;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.Queries;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/requests")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveApprovalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveApprovalsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("pending-approvals")]
    [RequirePermission(PermissionCatalog.Leave.Approve)]
    public async Task<IActionResult> PendingApprovals(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPendingLeaveApprovalsQuery(search, departmentId, leaveTypeId, fromDate, toDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("all")]
    [RequirePermission(PermissionCatalog.Leave.Read)]
    public async Task<IActionResult> All(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListAllLeaveRequestsQuery(search, departmentId, leaveTypeId, status, fromDate, toDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{requestId:guid}/approval")]
    [RequirePermission(PermissionCatalog.Leave.Approve)]
    public async Task<IActionResult> ApprovalDetail(Guid requestId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveApprovalDetailQuery(requestId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>HR's read-only view of any request from the All Requests ledger.</summary>
    [HttpGet("{requestId:guid}/detail")]
    [RequirePermission(PermissionCatalog.Leave.Read)]
    public async Task<IActionResult> Detail(Guid requestId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveApprovalDetailQuery(requestId, OrgWideRead: true), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/approve")]
    [RequirePermission(PermissionCatalog.Leave.Approve)]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] ApproveLeaveRequestRequest? request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(requestId, request?.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/reject")]
    [RequirePermission(PermissionCatalog.Leave.Approve)]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] RejectLeaveRequestRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RejectLeaveRequestCommand(requestId, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/change-decision")]
    [RequirePermission(PermissionCatalog.Leave.Approve)]
    public async Task<IActionResult> ChangeDecision(Guid requestId, [FromBody] ChangeLeaveDecisionRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ChangeLeaveDecisionCommand(requestId, request.Decision, request.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
