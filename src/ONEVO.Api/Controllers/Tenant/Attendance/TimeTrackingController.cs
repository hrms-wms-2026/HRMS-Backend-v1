using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.TimeTracking;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.Queries;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

[ApiController]
[Route("api/v1/attendance/time-tracking")]
[Authorize(Policy = "TenantPolicy")]
public sealed class TimeTrackingController(IMediator mediator) : ControllerBase
{
    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAttendanceTodayQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("clock-in")]
    public async Task<IActionResult> ClockIn(
        [FromBody] ClockInRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ClockInCommand(request.Source), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("clock-out")]
    public async Task<IActionResult> ClockOut(CancellationToken ct = default)
    {
        var result = await mediator.Send(new ClockOutCommand(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("break/start")]
    public async Task<IActionResult> StartBreak(CancellationToken ct = default)
    {
        var result = await mediator.Send(new StartBreakCommand(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("break/end")]
    public async Task<IActionResult> EndBreak(CancellationToken ct = default)
    {
        var result = await mediator.Send(new EndBreakCommand(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyAttendanceHistoryQuery(from, to, paging), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("covered-history")]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> CoveredHistory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetCoveredAttendanceHistoryQuery(from, to, employeeId, paging), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("history-detail")]
    public async Task<IActionResult> HistoryDetail(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAttendanceDayDetailQuery(employeeId, date), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
