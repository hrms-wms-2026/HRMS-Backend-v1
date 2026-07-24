namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ActivitySnapshotDto(
    Guid Id,
    DateTimeOffset CapturedAt,
    int KeyboardEventsCount,
    int MouseEventsCount,
    int ActiveSeconds,
    int IdleSeconds,
    decimal IntensityScore,
    string ForegroundProcessName);
