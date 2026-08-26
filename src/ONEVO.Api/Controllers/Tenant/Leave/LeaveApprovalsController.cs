using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Approvals;
using ONEVO.Api.Filters;
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
    [RequirePermission("leave:approve")]
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
    [RequirePermission("leave:read")]
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
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> ApprovalDetail(Guid requestId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveApprovalDetailQuery(requestId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/approve")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] ApproveLeaveRequestRequest? request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(requestId, request?.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/reject")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] RejectLeaveRequestRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RejectLeaveRequestCommand(requestId, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/request-info")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> RequestInfo(Guid requestId, [FromBody] RequestLeaveInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestLeaveInformationCommand(requestId, request.Question), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/respond-info")]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> RespondInfo(Guid requestId, [FromBody] RespondLeaveInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RespondLeaveInformationCommand(requestId, request.Message, request.FileRecordIds ?? []), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk-approve")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> BulkApprove([FromBody] BulkApproveLeaveRequestsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkApproveLeaveRequestsCommand(request.RequestIds, request.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk-reject")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> BulkReject([FromBody] BulkRejectLeaveRequestsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkRejectLeaveRequestsCommand(request.RequestIds, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
