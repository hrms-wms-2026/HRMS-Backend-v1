using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public record IngestDeviceStateSnapshotsCommand : IRequest<Result<SnapshotIngestOutcome>>
{
    public List<DeviceStateSnapshotItem> Snapshots { get; init; } = [];
}

public record DeviceStateSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public int IdleSeconds { get; init; }
    public bool IsIdle { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? AccuracyMeters { get; init; }
}
