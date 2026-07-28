using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ApproveRemoteLocationChange;
using ONEVO.Application.Features.TimeAttendance.Commands.ApproveWorkAreaChange;
using ONEVO.Application.Features.TimeAttendance.Commands.RejectRemoteLocationChange;
using ONEVO.Application.Features.TimeAttendance.Commands.RejectWorkAreaChange;
using ONEVO.Application.Features.TimeAttendance.Queries.GetPendingAttendanceApprovals;

namespace ONEVO.Api.Controllers.TimeAttendance;

[ApiController]
[Route("api/v1/time-attendance/approvals")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("attendance:approve")]
public sealed class AttendanceApprovalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AttendanceApprovalController(
        IMediator mediator,
        ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetPendingAttendanceApprovalsQuery(page, pageSize),
            ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpPut("work-area/{id:guid}/approve")]
    public Task<IActionResult> ApproveWorkArea(
        Guid id,
        [FromBody] ApprovalReviewRequest request,
        CancellationToken ct) =>
        ReviewWorkAreaAsync(id, request, approve: true, ct);

    [HttpPut("work-area/{id:guid}/reject")]
    public Task<IActionResult> RejectWorkArea(
        Guid id,
        [FromBody] ApprovalReviewRequest request,
        CancellationToken ct) =>
        ReviewWorkAreaAsync(id, request, approve: false, ct);

    [HttpPut("remote-location/{id:guid}/approve")]
    public Task<IActionResult> ApproveRemoteLocation(
        Guid id,
        [FromBody] ApprovalReviewRequest request,
        CancellationToken ct) =>
        ReviewRemoteAsync(id, request, approve: true, ct);

    [HttpPut("remote-location/{id:guid}/reject")]
    public Task<IActionResult> RejectRemoteLocation(
        Guid id,
        [FromBody] ApprovalReviewRequest request,
        CancellationToken ct) =>
        ReviewRemoteAsync(id, request, approve: false, ct);

    private async Task<IActionResult> ReviewWorkAreaAsync(
        Guid id,
        ApprovalReviewRequest request,
        bool approve,
        CancellationToken ct)
    {
        var result = approve
            ? await _mediator.Send(new ApproveWorkAreaChangeCommand(
                id,
                request.ExpectedVersion,
                request.ReviewComment,
                _currentUser.TenantId,
                _currentUser.UserId), ct)
            : await _mediator.Send(new RejectWorkAreaChangeCommand(
                id,
                request.ExpectedVersion,
                request.ReviewComment,
                _currentUser.TenantId,
                _currentUser.UserId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(new { status = approve ? "approved" : "rejected" });
    }

    private async Task<IActionResult> ReviewRemoteAsync(
        Guid id,
        ApprovalReviewRequest request,
        bool approve,
        CancellationToken ct)
    {
        var result = approve
            ? await _mediator.Send(new ApproveRemoteLocationChangeCommand(
                id,
                request.ExpectedVersion,
                request.ReviewComment,
                _currentUser.TenantId,
                _currentUser.UserId), ct)
            : await _mediator.Send(new RejectRemoteLocationChangeCommand(
                id,
                request.ExpectedVersion,
                request.ReviewComment,
                _currentUser.TenantId,
                _currentUser.UserId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(new { status = approve ? "approved" : "rejected" });
    }

    public sealed record ApprovalReviewRequest(
        uint ExpectedVersion,
        string? ReviewComment);
}
