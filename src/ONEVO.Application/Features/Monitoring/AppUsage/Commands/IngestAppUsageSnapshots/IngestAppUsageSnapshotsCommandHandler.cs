using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Mappers;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

public class IngestAppUsageSnapshotsCommandHandler
    : IRequestHandler<IngestAppUsageSnapshotsCommand, Result<SnapshotIngestOutcome>>
{
    private readonly IAppUsageSnapshotRepository _snapshots;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestAppUsageSnapshotsCommandHandler> _logger;
    private readonly IGlobalApplicationRepository _globalApplications;

    public IngestAppUsageSnapshotsCommandHandler(
        IAppUsageSnapshotRepository snapshots,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        IGlobalApplicationRepository globalApplications,
        ILogger<IngestAppUsageSnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeIdentity = employeeIdentity;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _globalApplications = globalApplications;
        _logger = logger;
    }

    public async Task<Result<SnapshotIngestOutcome>> Handle(
        IngestAppUsageSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<SnapshotIngestOutcome>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<SnapshotIngestOutcome>.Failure("Tenant not found.", 401);

        // Tray requests may hit the base host (system mode). Switch into the JWT
        // tenant so EF query filters + PostgreSQL RLS accept the write.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        var userId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, userId, MonitoringCapability.ApplicationTracking, cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "App-usage snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} UserId={UserId} Count={Count}",
                tenantId, agentDeviceId, userId, request.Snapshots.Count);
            return Result<SnapshotIngestOutcome>.Failure(MonitoringErrors.AppTrackingDisabled, 403, MonitoringErrors.AppTrackingDisabledCode);
        }

        // Resolves the real CoreHR Employee.Id to store, falling back to the raw UserId when no
        // Employee row exists yet - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, userId, _device.LegalEntityId, cancellationToken);

        // Judged per item: out-of-window items are skipped, the rest of the batch is still stored.
        var window = SnapshotTimeWindow.Partition(
            request.Snapshots.Select(s => s with { CapturedAt = SnapshotTimeWindow.Normalize(s.CapturedAt) }).ToList(),
            s => s.CapturedAt,
            now);
        if (window.AllRejected)
            return Result<SnapshotIngestOutcome>.Failure(window.RejectionError, 400, window.RejectionErrorCode);

        if (window.RejectedCount > 0)
        {
            _logger.LogWarning(
                "Skipped {Rejected} out-of-window app-usage snapshot(s) (too old={TooOld}, future={Future}); storing {Accepted}. TenantId={TenantId} DeviceId={DeviceId}",
                window.RejectedCount, window.TooOldCount, window.FutureCount, window.Accepted.Count,
                tenantId, agentDeviceId);
        }

        _logger.LogInformation(
            "App-usage snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            tenantId, agentDeviceId, employeeId, request.Snapshots.Count);

        // Idempotency: a retry after a lost 202 re-sends rows that are already stored.
        var alreadyStored = await _snapshots.GetExistingCapturedAtsAsync(
            tenantId, agentDeviceId, window.Accepted.Select(s => s.CapturedAt).ToList(), cancellationToken);
        var fresh = SnapshotTimeWindow.WithoutDuplicates(window.Accepted, s => s.CapturedAt, alreadyStored);

        var entities = fresh
            .Select(item => AppUsageSnapshotMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        if (entities.Count > 0)
        {
            await _snapshots.AddRangeAsync(entities, cancellationToken);
            await _globalApplications.RecordObservedAsync(
                entities.Where(x => x.ProcessName is not null).Select(x => x.ProcessName!),
                now,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SnapshotIngestOutcome>.Success(window.ToOutcome(entities.Count));
    }
}
