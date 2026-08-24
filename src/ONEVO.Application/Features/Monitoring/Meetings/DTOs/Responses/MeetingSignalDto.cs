namespace ONEVO.Application.Features.Monitoring.Meetings.DTOs.Responses;

public record MeetingSignalDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public bool IsMeetingAppRunning { get; init; }
    public string? ProcessName { get; init; }
}
