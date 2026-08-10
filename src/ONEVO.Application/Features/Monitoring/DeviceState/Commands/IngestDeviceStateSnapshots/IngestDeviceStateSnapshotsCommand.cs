using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public record IngestDeviceStateSnapshotsCommand : IRequest<Result>
{
    public List<DeviceStateSnapshotItem> Snapshots { get; init; } = [];
}

public record DeviceStateSnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public int IdleSeconds { get; init; }
    public bool IsIdle { get; init; }
}
