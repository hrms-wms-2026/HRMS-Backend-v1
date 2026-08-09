using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.DeviceState;

/// <summary>
/// Tray App → Backend ingest for device idle/active state.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/device-state")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringDeviceStateIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringDeviceStateIngestController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Accept a batch of device-state snapshots from the tray agent.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestDeviceStateSnapshotsRequest request,
        CancellationToken ct)
    {
        var items = (request.Snapshots ?? [])
            .Select(s => new DeviceStateSnapshotItem
            {
                CapturedAt = s.CapturedAt,
                IdleSeconds = s.IdleSeconds,
                IsIdle = s.IsIdle
            })
            .ToList();

        var result = await _mediator.Send(
            new IngestDeviceStateSnapshotsCommand { Snapshots = items },
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record IngestDeviceStateSnapshotsRequest(
    [property: JsonPropertyName("snapshots")] List<DeviceStateSnapshotRequestItem>? Snapshots);

public record DeviceStateSnapshotRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("idle_seconds")] int IdleSeconds,
    [property: JsonPropertyName("is_idle")] bool IsIdle);
