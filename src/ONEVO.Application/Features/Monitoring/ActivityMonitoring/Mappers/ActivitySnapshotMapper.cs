using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Mappers;

public static class ActivitySnapshotMapper
{
    public static ActivitySnapshot ToEntity(
        ActivitySnapshotItem item,
        Guid tenantId,
        Guid employeeId,
        Guid agentDeviceId,
        DateTimeOffset createdAt)
    {
        return new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = agentDeviceId,
            CapturedAt = item.CapturedAt,
            KeyboardEventsCount = item.KeyboardEventsCount,
            MouseEventsCount = item.MouseEventsCount,
            ActiveSeconds = item.ActiveSeconds,
            IdleSeconds = item.IdleSeconds,
            IntensityScore = item.IntensityScore,
            ForegroundProcessName = item.ForegroundProcessName,
            CreatedAt = createdAt
        };
    }
}
