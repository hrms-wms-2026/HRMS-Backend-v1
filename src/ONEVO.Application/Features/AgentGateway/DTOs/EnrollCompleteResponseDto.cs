namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record EnrollCompleteResponseDto(
    Guid AgentId,
    Guid TenantId,
    Guid EmployeeId,
    string EmployeeName,
    string DeviceToken,
    DateTimeOffset TokenExpiresAt,
    string PolicyJson,
    string DeviceApprovalStatus,
    Guid? DeviceChangeRequestId
);
