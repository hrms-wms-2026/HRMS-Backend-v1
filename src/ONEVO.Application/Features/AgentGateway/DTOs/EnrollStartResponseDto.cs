namespace ONEVO.Application.Features.AgentGateway.DTOs;

public record EnrollStartResponseDto(
    Guid EnrollmentId,
    string AuthUrl,
    DateTimeOffset ExpiresAt
);
