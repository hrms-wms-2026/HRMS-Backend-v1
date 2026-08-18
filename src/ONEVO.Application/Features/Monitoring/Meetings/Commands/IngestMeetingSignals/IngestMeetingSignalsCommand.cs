using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public record IngestMeetingSignalsCommand : IRequest<Result>
{
    public List<MeetingSignalItem> Signals { get; init; } = [];
}

public record MeetingSignalItem
{
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
