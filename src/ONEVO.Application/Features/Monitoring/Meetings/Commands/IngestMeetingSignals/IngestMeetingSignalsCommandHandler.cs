using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Mappers;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public class IngestMeetingSignalsCommandHandler : IRequestHandler<IngestMeetingSignalsCommand, Result<SnapshotIngestOutcome>>
{
    private readonly IMeetingSignalRepository _signals;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestMeetingSignalsCommandHandler> _logger;

    public IngestMeetingSignalsCommandHandler(
        IMeetingSignalRepository signals,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestMeetingSignalsCommandHandler> logger)
    {
        _signals = signals;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeIdentity = employeeIdentity;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SnapshotIngestOutcome>> Handle(IngestMeetingSignalsCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<SnapshotIngestOutcome>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result<SnapshotIngestOutcome>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var tenantId = _device.TenantId;
        var userId = _device.UserId;
        var agentDeviceId = _device.DeviceRegistrationId;
        var now = _clock.UtcNow;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId, userId, MonitoringCapability.MeetingDetection, ct);

        if (!enabled)
        {
            _logger.LogInformation(
                "Meeting-signal batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} UserId={UserId} Count={Count}",
                tenantId, agentDeviceId, userId, request.Signals.Count);
            return Result<SnapshotIngestOutcome>.Failure(MonitoringErrors.MeetingDetectionDisabled, 403, MonitoringErrors.MeetingDetectionDisabledCode);
        }

        // Resolves the real CoreHR Employee.Id to store, falling back to the raw UserId when no
        // Employee row exists yet - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, userId, _device.LegalEntityId, ct);

        // Judged per item: out-of-window items are skipped, the rest of the batch is still stored.
        var window = SnapshotTimeWindow.Partition(
            request.Signals.Select(s => s with { CapturedAt = SnapshotTimeWindow.Normalize(s.CapturedAt) }).ToList(),
            s => s.CapturedAt,
            now);
        if (window.AllRejected)
            return Result<SnapshotIngestOutcome>.Failure(window.RejectionError, 400, window.RejectionErrorCode);

        if (window.RejectedCount > 0)
        {
            _logger.LogWarning(
                "Skipped {Rejected} out-of-window meeting signal(s) (too old={TooOld}, future={Future}); storing {Accepted}. TenantId={TenantId} DeviceId={DeviceId}",
                window.RejectedCount, window.TooOldCount, window.FutureCount, window.Accepted.Count,
                tenantId, agentDeviceId);
        }

        // Idempotency: a retry after a lost 202 re-sends rows that are already stored.
        var alreadyStored = await _signals.GetExistingCapturedAtsAsync(
            tenantId, agentDeviceId, window.Accepted.Select(s => s.CapturedAt).ToList(), ct);
        var fresh = SnapshotTimeWindow.WithoutDuplicates(window.Accepted, s => s.CapturedAt, alreadyStored);

        var entities = fresh
            .Select(item => MeetingSignalMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        if (entities.Count > 0)
        {
            await _signals.AddRangeAsync(entities, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return Result<SnapshotIngestOutcome>.Success(window.ToOutcome(entities.Count));
    }
}
