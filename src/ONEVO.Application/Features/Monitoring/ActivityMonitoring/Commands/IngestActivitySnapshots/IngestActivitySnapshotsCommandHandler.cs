using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Mappers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

public class IngestActivitySnapshotsCommandHandler
    : IRequestHandler<IngestActivitySnapshotsCommand, Result>
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
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        IngestActivitySnapshotsCommand request,
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
            tenantId,
            employeeId,
            MonitoringCapability.ActivityMonitoring,
            cancellationToken);

        if (!enabled)
        {
            _logger.LogInformation(
                "Activity snapshot batch rejected: monitoring disabled. TenantId={TenantId} DeviceId={DeviceId} EmployeeId={EmployeeId} Count={Count}",
                tenantId,
                agentDeviceId,
                employeeId,
                request.Snapshots.Count);
            return Result.Failure(MonitoringErrors.ActivityMonitoringDisabled, 403);
        }

        // Time-window validation (depends on server clock — kept out of FluentValidation).
        foreach (var item in request.Snapshots)
        {
            if (item.CapturedAt > now.AddMinutes(5))
                return Result.Failure(MonitoringErrors.SnapshotFutureTime, 400);

            if (item.CapturedAt < now.AddHours(-24))
                return Result.Failure(MonitoringErrors.SnapshotTooOld, 400);
        }

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

        var entities = request.Snapshots
            .Select(item => ActivitySnapshotMapper.ToEntity(
                item, tenantId, employeeId, agentDeviceId, now))
            .ToList();

        await _snapshots.AddRangeAsync(entities, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
