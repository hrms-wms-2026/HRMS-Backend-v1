using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Meetings;

/// <summary>Tray App → Backend ingest for probabilistic meeting-app-presence samples.</summary>
[ApiController]
[Route("api/v1/monitoring/meetings")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringMeetingIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringMeetingIngestController(IMediator mediator) => _mediator = mediator;

    [HttpPost("signals")]
    public async Task<IActionResult> IngestSignals(
        [FromBody] IngestMeetingSignalsRequest request, CancellationToken ct)
    {
        var items = (request.Signals ?? [])
            .Select(s => new MeetingSignalItem
            {
                CapturedAt = s.CapturedAt,
                IsMeetingAppRunning = s.IsMeetingAppRunning,
                ProcessName = s.ProcessName
            })
            .ToList();

        var result = await _mediator.Send(new IngestMeetingSignalsCommand { Signals = items }, ct);

        return result.IsSuccess ? Accepted() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}

public record IngestMeetingSignalsRequest(
    [property: JsonPropertyName("signals")] List<MeetingSignalRequestItem>? Signals);

public record MeetingSignalRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("is_meeting_app_running")] bool IsMeetingAppRunning,
    [property: JsonPropertyName("process_name")] string? ProcessName);
