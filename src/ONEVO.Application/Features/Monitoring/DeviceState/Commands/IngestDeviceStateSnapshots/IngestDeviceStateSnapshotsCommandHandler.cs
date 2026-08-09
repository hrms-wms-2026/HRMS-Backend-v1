using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.Mappers;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

public class IngestDeviceStateSnapshotsCommandHandler
    : IRequestHandler<IngestDeviceStateSnapshotsCommand, Result>
{
    private readonly IDeviceStateSnapshotRepository _snapshots;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestDeviceStateSnapshotsCommandHandler> _logger;

    public IngestDeviceStateSnapshotsCommandHandler(
        IDeviceStateSnapshotRepository snapshots,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestDeviceStateSnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        IngestDeviceStateSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

        // Tray requests may hit the base host (system mode). Switch into the JWT
        // tenant so EF query filters + PostgreSQL RLS accept the write.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        // Phase 1: tray JWT binds to UserId; EmployeeId column stores that identity
        // until CoreHR employee master is always present for activated devices.
        var employeeId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.DeviceTracking, cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "Device-state snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
                tenantId, agentDeviceId, employeeId, request.Snapshots.Count);
            return Result.Failure(MonitoringErrors.DeviceTrackingDisabled, 403);
        }

        foreach (var item in request.Snapshots)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);

            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

        _logger.LogInformation(
            "Device-state snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            tenantId, agentDeviceId, employeeId, request.Snapshots.Count);

        var entities = request.Snapshots
            .Select(item => DeviceStateSnapshotMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _snapshots.AddRangeAsync(entities, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
