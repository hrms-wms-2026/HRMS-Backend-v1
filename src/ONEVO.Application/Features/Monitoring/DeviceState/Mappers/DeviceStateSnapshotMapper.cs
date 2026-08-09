using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Mappers;

public static class DeviceStateSnapshotMapper
{
    public static DeviceStateSnapshot ToEntity(
        DeviceStateSnapshotItem item,
        Guid tenantId,
        Guid employeeId,
        Guid agentDeviceId,
        DateTimeOffset createdAt)
    {
        return new DeviceStateSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = agentDeviceId,
            CapturedAt = item.CapturedAt,
            IdleSeconds = item.IdleSeconds,
            IsIdle = item.IsIdle,
            CreatedAt = createdAt
        };
    }
}
