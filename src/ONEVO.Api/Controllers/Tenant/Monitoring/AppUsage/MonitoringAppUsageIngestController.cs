using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.AppUsage;

/// <summary>
/// Tray App → Backend ingest for foreground application usage.
/// Window titles arrive pre-hashed — this endpoint never receives raw title text.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/app-usage")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringAppUsageIngestController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringAppUsageIngestController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Accept a batch of app-usage snapshots from the tray agent.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("snapshots")]
    public async Task<IActionResult> IngestSnapshots(
        [FromBody] IngestAppUsageSnapshotsRequest request,
        CancellationToken ct)
    {
        var items = (request.Snapshots ?? [])
            .Select(s => new AppUsageSnapshotItem
            {
                CapturedAt = s.CapturedAt,
                ProcessName = s.ProcessName,
                WindowTitleHash = s.WindowTitleHash
            })
            .ToList();

        var result = await _mediator.Send(
            new IngestAppUsageSnapshotsCommand { Snapshots = items },
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Accepted();
    }
}

public record IngestAppUsageSnapshotsRequest(
    [property: JsonPropertyName("snapshots")] List<AppUsageSnapshotRequestItem>? Snapshots);

public record AppUsageSnapshotRequestItem(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("process_name")] string? ProcessName,
    [property: JsonPropertyName("window_title_hash")] string? WindowTitleHash);
