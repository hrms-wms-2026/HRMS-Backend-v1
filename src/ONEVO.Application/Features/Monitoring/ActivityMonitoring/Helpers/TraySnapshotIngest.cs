using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

/// <summary>Which tray snapshot is being ingested: its log name and the monitoring switch that allows it.</summary>
public sealed record TraySnapshotKind(
    string Name, MonitoringCapability Capability, string DisabledError, string DisabledErrorCode);

/// <summary>A batch that passed the common checks: who sent it, and which items fit the time window.</summary>
public sealed record TraySnapshotBatch<T>(
    Guid TenantId,
    Guid UserId,
    Guid EmployeeId,
    Guid AgentDeviceId,
    DateTimeOffset Now,
    SnapshotTimeWindowResult<T> Window);

/// <summary>
/// The steps every tray snapshot upload (activity, app usage, device state) shares (Improvements
/// task 35): check the device token, switch into the token's tenant, check the monitoring switch,
/// resolve the employee, keep the items inside the time window, and drop captures this device
/// already sent. Each handler keeps only its own mapping and save.
/// </summary>
public sealed class TraySnapshotIngest(
    ITrayCurrentDevice device,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IMonitoringToggleResolver toggleResolver,
    ITrayEmployeeIdentityResolver employeeIdentity,
    IDateTimeProvider clock,
    ILogger<TraySnapshotIngest> logger)
{
    public async Task<Result<TraySnapshotBatch<T>>> BeginAsync<T>(
        TraySnapshotKind kind,
        IReadOnlyList<T> items,
        Func<T, DateTimeOffset> capturedAt,
        Func<T, DateTimeOffset, T> withCapturedAt,
        CancellationToken ct)
    {
        if (!device.IsAuthenticated
            || device.TenantId == Guid.Empty
            || device.UserId == Guid.Empty
            || device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<TraySnapshotBatch<T>>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return Result<TraySnapshotBatch<T>>.Failure("Tenant not found.", 401);

        // Tray requests may hit the base host (system mode). Switch into the JWT tenant so the
        // query filters and row-level security accept the write.
        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var tenantId = device.TenantId;
        var userId = device.UserId;
        var agentDeviceId = device.DeviceRegistrationId;
        var now = clock.UtcNow;

        if (!await toggleResolver.IsEnabledAsync(tenantId, userId, kind.Capability, ct))
        {
            logger.LogInformation(
                "{Kind} snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} UserId={UserId} Count={Count}",
                kind.Name, tenantId, agentDeviceId, userId, items.Count);
            return Result<TraySnapshotBatch<T>>.Failure(kind.DisabledError, 403, kind.DisabledErrorCode);
        }

        // The real CoreHR Employee.Id, falling back to the raw UserId when no Employee row exists
        // yet - see ITrayEmployeeIdentityResolver.
        var employeeId = await employeeIdentity.ResolveEmployeeIdAsync(tenantId, userId, device.LegalEntityId, ct);

        // Judged per item: out-of-window items are skipped, the rest of the batch is still stored.
        var window = SnapshotTimeWindow.Partition(
            items.Select(item => withCapturedAt(item, SnapshotTimeWindow.Normalize(capturedAt(item)))).ToList(),
            capturedAt,
            now);
        if (window.AllRejected)
            return Result<TraySnapshotBatch<T>>.Failure(window.RejectionError, 400, window.RejectionErrorCode);

        if (window.RejectedCount > 0)
        {
            logger.LogWarning(
                "Skipped {Rejected} out-of-window {Kind} snapshot(s) (too old={TooOld}, future={Future}); storing {Accepted}. TenantId={TenantId} DeviceId={DeviceId}",
                window.RejectedCount, kind.Name, window.TooOldCount, window.FutureCount, window.Accepted.Count,
                tenantId, agentDeviceId);
        }

        logger.LogInformation(
            "{Kind} snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            kind.Name, tenantId, agentDeviceId, employeeId, items.Count);

        return Result<TraySnapshotBatch<T>>.Success(
            new TraySnapshotBatch<T>(tenantId, userId, employeeId, agentDeviceId, now, window));
    }

    /// <summary>
    /// The in-window items this device has not sent before. A tray retry after a lost 202 re-sends
    /// rows that are already stored; storing them twice would inflate the daily totals. Since task 35
    /// a unique index on (tenant_id, agent_device_id, captured_at) backs this up in all three tables.
    /// </summary>
    public List<T> WithoutStored<T>(
        TraySnapshotKind kind,
        TraySnapshotBatch<T> batch,
        IReadOnlySet<DateTimeOffset> alreadyStored,
        Func<T, DateTimeOffset> capturedAt)
    {
        var fresh = SnapshotTimeWindow.WithoutDuplicates(batch.Window.Accepted, capturedAt, alreadyStored);
        if (fresh.Count < batch.Window.Accepted.Count)
        {
            logger.LogInformation(
                "Skipped {DuplicateCount} already-ingested {Kind} snapshot(s) in this batch. TenantId={TenantId} DeviceId={DeviceId}",
                batch.Window.Accepted.Count - fresh.Count, kind.Name, batch.TenantId, batch.AgentDeviceId);
        }

        return fresh;
    }
}
