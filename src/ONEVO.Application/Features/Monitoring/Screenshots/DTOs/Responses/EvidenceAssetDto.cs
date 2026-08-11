namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

public record EvidenceAssetDto(
    Guid Id,
    Guid EmployeeId,
    Guid? AgentDeviceId,
    string EvidenceType,
    string TriggerType,
    DateTimeOffset CapturedAt,
    DateTimeOffset CreatedAt);
