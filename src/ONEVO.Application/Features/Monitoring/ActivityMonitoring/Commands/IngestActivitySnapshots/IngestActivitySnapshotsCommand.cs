using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

public record IngestActivitySnapshotsCommand : IRequest<Result>
{
    public List<ActivitySnapshotItem> Snapshots { get; init; } = [];
}

public record ActivitySnapshotItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public int KeyboardEventsCount { get; init; }
    public int MouseEventsCount { get; init; }
    public int ActiveSeconds { get; init; }
    public int IdleSeconds { get; init; }
    public decimal IntensityScore { get; init; }
    public string? ForegroundProcessName { get; init; }
}
