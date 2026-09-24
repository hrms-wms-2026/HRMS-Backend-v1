using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Mappers;

public static class AppUsageSnapshotMapper
{
    public static AppUsageSnapshot ToEntity(
        AppUsageSnapshotItem item,
        Guid tenantId,
        Guid employeeId,
        Guid agentDeviceId,
        DateTimeOffset createdAt)
    {
        return new AppUsageSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = agentDeviceId,
            CapturedAt = item.CapturedAt,
            ProcessName = item.ProcessName,
            WindowTitleHash = item.WindowTitleHash,
            CreatedAt = createdAt
        };
    }
}
