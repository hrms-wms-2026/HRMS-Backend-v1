using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public record IngestMeetingSignalsCommand : IRequest<Result<SnapshotIngestOutcome>>
{
    public List<MeetingSignalItem> Signals { get; init; } = [];
}

public record MeetingSignalItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
