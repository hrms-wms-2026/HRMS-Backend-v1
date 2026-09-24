namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;

public record RequestScreenshotRequest(
    Guid AgentDeviceId,
    string? Note);
