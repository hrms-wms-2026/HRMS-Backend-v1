using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.WorkSessions.OutboxPayloads;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.OutboxHandlers;

public sealed class MonitoringWorkSessionCompletedOutboxHandler : IOutboxMessageHandler
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringReportTimeZoneResolver _timeZoneResolver;
    private readonly IActivityDailySummaryRebuilder _rebuilder;

    public MonitoringWorkSessionCompletedOutboxHandler(
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringReportTimeZoneResolver timeZoneResolver,
        IActivityDailySummaryRebuilder rebuilder)
    {
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _timeZoneResolver = timeZoneResolver;
        _rebuilder = rebuilder;
    }

    public string Type => OutboxMessageTypes.MonitoringWorkSessionCompleted;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<MonitoringWorkSessionCompletedPayload>(payloadJson)
            ?? throw new InvalidOperationException("monitoring_work_session_completed payload is empty.");

        var tenant = await _tenants.GetByIdAsync(payload.TenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {payload.TenantId} not found for outbox finalization.");

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            ct);

        var timeZone = await _timeZoneResolver.ResolveAsync(
            payload.TenantId, payload.EmployeeId, ct);

        var localDate = MonitoringReportDateRange.ToLocalDate(payload.ClockOutAt, timeZone);

        await _rebuilder.RebuildAsync(
            payload.TenantId, payload.EmployeeId, localDate, ct);
    }
}
