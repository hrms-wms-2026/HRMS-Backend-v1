using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.WorkSessions.Commands.SubmitWorkSession;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.WorkSessions;

[ApiController]
[Route("api/v1/monitoring/work-sessions")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringWorkSessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringWorkSessionsController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Submit a completed clock-in/break/clock-out session. Called by the Agent
    /// Service once at clock-out; upserted by session_id so retries are safe.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitWorkSession(
        [FromBody] SubmitWorkSessionRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitWorkSessionCommand(
            request.SessionId,
            request.ClockInAt,
            request.ClockOutAt,
            request.AccumulatedBreakSeconds,
            request.AccumulatedWorkSeconds,
            request.BreakSessionCount,
            request.ScheduleDisplay), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(result.Value);
    }
}

public record SubmitWorkSessionRequest(
    [property: JsonPropertyName("session_id")] Guid SessionId,
    [property: JsonPropertyName("clock_in_at")] DateTimeOffset ClockInAt,
    [property: JsonPropertyName("clock_out_at")] DateTimeOffset ClockOutAt,
    [property: JsonPropertyName("accumulated_break_seconds")] int AccumulatedBreakSeconds,
    [property: JsonPropertyName("accumulated_work_seconds")] int AccumulatedWorkSeconds,
    [property: JsonPropertyName("break_session_count")] int BreakSessionCount,
    [property: JsonPropertyName("schedule_display")] string? ScheduleDisplay);
