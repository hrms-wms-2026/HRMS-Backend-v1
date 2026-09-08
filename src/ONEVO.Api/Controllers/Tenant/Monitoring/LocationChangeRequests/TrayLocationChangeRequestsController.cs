using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.LocationChangeRequests;

/// <summary>
/// Tray-authenticated endpoints for the "Request location change" action and the post-clock-in
/// "save this as your new location?" prompt. Mirrors MonitoringCheckInController's auth
/// convention (tray device JWT, not a tenant user session).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/location-change-requests")]
[Authorize(Policy = "TrayDevicePolicy")]
public sealed class TrayLocationChangeRequestsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit(
        [FromBody] LocationChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new TraySubmitLocationChangeRequestCommand(
            request.Latitude, request.Longitude, request.AccuracyMeters, request.Reason), ct);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/respond")]
    public async Task<IActionResult> Respond(
        Guid id, [FromBody] RespondToLocationChangeRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new TrayRespondToLocationChangeRequestCommand(id, request.Apply), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetPendingLocationChangeDecisionQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
