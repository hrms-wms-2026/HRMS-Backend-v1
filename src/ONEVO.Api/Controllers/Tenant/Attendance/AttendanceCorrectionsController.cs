using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using ONEVO.Api.Contracts.Attendance.Corrections;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;
using ONEVO.Application.Features.TimeAttendance.Queries.AttendanceCorrections;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

[ApiController]
[Route("api/v1/attendance/corrections")]
[Authorize(Policy = "TenantPolicy")]
public sealed class AttendanceCorrectionsController(IMediator mediator) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromBody] RequestAttendanceCorrectionRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new PreviewAttendanceCorrectionCommand(
            request.WorkDate, request.CorrectionType, request.RequestedClockInAt,
            request.RequestedClockOutAt, ToInputBreaks(request.RequestedBreaks), request.Reason, request.Notes), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    public async Task<IActionResult> RequestCorrection(
        [FromBody] RequestAttendanceCorrectionRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RequestAttendanceCorrectionCommand(
            request.WorkDate, request.CorrectionType, request.RequestedClockInAt,
            request.RequestedClockOutAt, ToInputBreaks(request.RequestedBreaks), request.Reason, request.Notes), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyAttendanceCorrectionsQuery(from, to, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("approvals")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approvals(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListAttendanceCorrectionApprovalsQuery(from, to, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ReviewAttendanceCorrectionRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveAttendanceCorrectionCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("attendance:approve")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] ReviewAttendanceCorrectionRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectAttendanceCorrectionCommand(id, request.ReviewComment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CancelAttendanceCorrectionCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private static IReadOnlyList<AttendanceCorrectionInputBreak>? ToInputBreaks(
        IReadOnlyList<AttendanceCorrectionBreakRequest>? breaks)
        => breaks?.Select(x => new AttendanceCorrectionInputBreak(x.BreakStart, x.BreakEnd, x.BreakType)).ToArray();
}
