using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.ActivityMonitoring;

/// <summary>
/// Tray App → Backend ingest for keyboard/mouse activity counts (never keystroke content).
/// </summary>
[ApiController]
[Route("api/v1/monitoring/activity")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringActivityIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringActivityIngestController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Accept a batch of activity snapshots from the tray agent.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestActivitySnapshotsRequest request,
        CancellationToken ct)
    {
        var items = (request.Snapshots ?? [])
            .Select(s => new ActivitySnapshotItem
            {
                CapturedAt = s.CapturedAt,
                KeyboardEventsCount = s.KeyboardEventsCount,
                MouseEventsCount = s.MouseEventsCount,
                ActiveSeconds = s.ActiveSeconds,
                IdleSeconds = s.IdleSeconds,
                IntensityScore = s.IntensityScore,
                ForegroundProcessName = s.ForegroundProcessName
            })
            .ToList();

        var result = await _mediator.Send(
            new IngestActivitySnapshotsCommand { Snapshots = items },
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record IngestActivitySnapshotsRequest(
    [property: JsonPropertyName("snapshots")] List<ActivitySnapshotRequestItem>? Snapshots);

public record ActivitySnapshotRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("keyboard_events_count")] int KeyboardEventsCount,
    [property: JsonPropertyName("mouse_events_count")] int MouseEventsCount,
    [property: JsonPropertyName("active_seconds")] int ActiveSeconds,
    [property: JsonPropertyName("idle_seconds")] int IdleSeconds,
    [property: JsonPropertyName("intensity_score")] decimal IntensityScore,
    [property: JsonPropertyName("foreground_process_name")] string? ForegroundProcessName);
