using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Mappers;

public static class EvidenceAssetMapper
{
    public static EvidenceAssetDto ToDto(MonitoringEvidenceAsset a) => new(
        a.Id,
        a.EmployeeId,
        a.AgentDeviceId,
        a.EvidenceType,
        a.TriggerType,
        a.CapturedAt,
        a.CreatedAt);
}
