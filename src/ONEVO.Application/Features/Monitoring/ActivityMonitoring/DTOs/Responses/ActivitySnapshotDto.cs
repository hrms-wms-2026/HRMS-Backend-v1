namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public record ActivitySnapshotDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int KeyboardEventsCount { get; init; }
    public int MouseEventsCount { get; init; }
    public int ActiveSeconds { get; init; }
    public int IdleSeconds { get; init; }
    public decimal IntensityScore { get; init; }
    public string? ForegroundProcess { get; init; }
}
