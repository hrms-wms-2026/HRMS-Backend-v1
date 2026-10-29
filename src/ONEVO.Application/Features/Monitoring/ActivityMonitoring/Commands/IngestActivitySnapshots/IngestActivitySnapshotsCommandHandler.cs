using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Mappers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

public class IngestActivitySnapshotsCommandHandler
    : IRequestHandler<IngestActivitySnapshotsCommand, Result<SnapshotIngestOutcome>>
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IActivitySnapshotRepository _snapshots;
    private readonly IActivityRawBufferRepository _rawBuffer;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IngestActivitySnapshotsCommandHandler> _logger;

    public IngestActivitySnapshotsCommandHandler(
        IActivitySnapshotRepository snapshots,
        IActivityRawBufferRepository rawBuffer,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<IngestActivitySnapshotsCommandHandler> logger)
    {
        _snapshots = snapshots;
        _rawBuffer = rawBuffer;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeIdentity = employeeIdentity;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SnapshotIngestOutcome>> Handle(
        IngestActivitySnapshotsCommand request,
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
            tenantId,
            userId,
            MonitoringCapability.ActivityMonitoring,
            cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "Activity snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} UserId={UserId} Count={Count}",
                tenantId,
                agentDeviceId,
                userId,
                request.Snapshots.Count);
            return Result<SnapshotIngestOutcome>.Failure(MonitoringErrors.ActivityMonitoringDisabled, 403, MonitoringErrors.ActivityMonitoringDisabledCode);
        }

        // Resolves the real CoreHR Employee.Id to store, falling back to the raw UserId when no
        // Employee row exists yet - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, userId, _device.LegalEntityId, cancellationToken);

        // Time-window validation (depends on server clock — kept out of FluentValidation).
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
                "Skipped {Rejected} out-of-window activity snapshot(s) (too old={TooOld}, future={Future}); storing {Accepted}. TenantId={TenantId} DeviceId={DeviceId}",
                window.RejectedCount, window.TooOldCount, window.FutureCount, window.Accepted.Count,
                tenantId, agentDeviceId);
        }

        var inWindow = window.Accepted;

        _logger.LogInformation(
            "Activity snapshot batch received. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
            tenantId,
            agentDeviceId,
            employeeId,
            request.Snapshots.Count);

        if (request.Snapshots.Count > 50)
        {
            _logger.LogWarning(
                "Large activity snapshot batch. TenantId={TenantId} DeviceId={DeviceId} Count={Count}",
                tenantId,
                agentDeviceId,
                request.Snapshots.Count);
        }

        // Raw buffer stores the batch for audit/replay — never log payload content.
        var payloadJson = JsonSerializer.Serialize(request.Snapshots, PayloadJsonOptions);
        await _rawBuffer.AddAsync(new ActivityRawBuffer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentDeviceId = agentDeviceId,
            ReceivedAt = now,
            PayloadJson = payloadJson
        }, cancellationToken);

        // Idempotency: a tray retry/resend after a slow or dropped response re-sends the same
        // capture interval. GetMyWorkPatternQueryHandler sums ActiveSeconds/IdleSeconds across
        // every row for the day, so inserting the same interval twice silently inflates it -
        // skip anything this device has already reported before mapping/inserting.
        var alreadyCaptured = await _snapshots.GetExistingCapturedAtsAsync(
            tenantId, agentDeviceId, inWindow.Select(s => s.CapturedAt).ToList(), cancellationToken);

        var newSnapshots = SnapshotTimeWindow.WithoutDuplicates(inWindow, s => s.CapturedAt, alreadyCaptured);

        if (newSnapshots.Count < inWindow.Count)
        {
            _logger.LogInformation(
                "Skipped {DuplicateCount} already-ingested snapshot(s) in this batch. TenantId={TenantId} DeviceId={DeviceId}",
                inWindow.Count - newSnapshots.Count,
                tenantId,
                agentDeviceId);
        }

        var entities = newSnapshots
            .Select(item => ActivitySnapshotMapper.ToEntity(
                item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        if (entities.Count > 0)
        {
            await _snapshots.AddRangeAsync(entities, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<SnapshotIngestOutcome>.Success(window.ToOutcome(entities.Count));
    }
}
