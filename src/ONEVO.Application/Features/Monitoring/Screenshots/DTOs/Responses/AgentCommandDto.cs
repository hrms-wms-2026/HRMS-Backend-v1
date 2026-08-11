namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

public record AgentCommandDto(
    Guid Id,
    string CommandType,
    string PayloadJson,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
