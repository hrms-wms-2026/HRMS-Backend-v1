namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record AgentLoginResponseDto(
    Guid EmployeeId,
    string EmployeeName,
    string PolicyJson
);
