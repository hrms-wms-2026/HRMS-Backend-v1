namespace ONEVO.Application.Features.Monitoring.WorkSessions.OutboxPayloads;

public sealed record MonitoringWorkSessionCompletedPayload(
    Guid WorkSessionId,
    Guid TenantId,
    Guid EmployeeId,
    DateTimeOffset ClockOutAt);
