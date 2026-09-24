using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Mappers;

public static class AgentCommandMapper
{
    public static AgentCommandDto ToDto(AgentCommand c) => new(
        c.Id,
        c.CommandType,
        c.PayloadJson,
        c.Status,
        c.ExpiresAt,
        c.CreatedAt);
}
