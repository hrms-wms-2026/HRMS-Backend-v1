namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;

public record CompleteCommandRequest(
    bool Success,
    string? ResultJson,
    Guid? FileRecordId,
    DateTimeOffset CapturedAt);
