using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.Mappers;

public static class MeetingSignalMapper
{
    public static MeetingSignal ToEntity(
        MeetingSignalItem item, Guid tenantId, Guid employeeId, Guid agentDeviceId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        AgentDeviceId = agentDeviceId,
        CapturedAt = item.CapturedAt,
        IsMeetingAppRunning = item.IsMeetingAppRunning,
        ProcessName = item.ProcessName,
        CreatedAt = now
    };
}
