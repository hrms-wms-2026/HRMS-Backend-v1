using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Mappers;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Errors;

namespace ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;

public class IngestMeetingSignalsCommandHandler : IRequestHandler<IngestMeetingSignalsCommand, Result>
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

    public async Task<Result> Handle(IngestMeetingSignalsCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result.Failure("Tenant not found.", 401);

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
            return Result.Failure(MonitoringErrors.MeetingDetectionDisabled, 403);
        }

        // Resolves the real CoreHR Employee.Id to store, falling back to the raw UserId when no
        // Employee row exists yet - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, userId, _device.LegalEntityId, ct);

        foreach (var item in request.Signals)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);
            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

        var entities = request.Signals
            .Select(item => MeetingSignalMapper.ToEntity(item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _signals.AddRangeAsync(entities, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
