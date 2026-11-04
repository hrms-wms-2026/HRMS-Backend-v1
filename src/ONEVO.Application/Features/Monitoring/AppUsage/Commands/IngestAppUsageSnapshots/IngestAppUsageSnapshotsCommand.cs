using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public record IngestAppUsageSnapshotsCommand : IRequest<Result<SnapshotIngestOutcome>>
{
    public List<AppUsageSnapshotItem> Snapshots { get; init; } = [];
}

public record AppUsageSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitleHash { get; init; }
}
