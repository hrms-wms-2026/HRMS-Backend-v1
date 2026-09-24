using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public record IngestAppUsageSnapshotsCommand : IRequest<Result>
{
    public List<AppUsageSnapshotItem> Snapshots { get; init; } = [];
}

public record AppUsageSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitleHash { get; init; }
}
