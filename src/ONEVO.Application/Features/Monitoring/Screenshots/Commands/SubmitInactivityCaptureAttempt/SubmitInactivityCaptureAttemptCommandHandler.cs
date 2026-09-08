using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

public class SubmitInactivityCaptureAttemptCommandHandler
    : IRequestHandler<SubmitInactivityCaptureAttemptCommand, Result<Guid>>
{
    private readonly IFileStorageService _fileStorage;
    private readonly IEvidenceAssetRepository _assets;
    private readonly IInactivityCaptureAttemptRepository _attempts;
    private readonly ITrayActivationRepository _trayRepo;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringToggleResolver _toggles;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitInactivityCaptureAttemptCommandHandler> _logger;

    public SubmitInactivityCaptureAttemptCommandHandler(
        IFileStorageService fileStorage,
        IEvidenceAssetRepository assets,
        IInactivityCaptureAttemptRepository attempts,
        ITrayActivationRepository trayRepo,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringToggleResolver toggles,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<SubmitInactivityCaptureAttemptCommandHandler> logger)
    {
        _fileStorage = fileStorage;
        _assets = assets;
        _attempts = attempts;
        _trayRepo = trayRepo;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _toggles = toggles;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(SubmitInactivityCaptureAttemptCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<Guid>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenant not found.", 401);

        // Tray requests hit the base host (system mode), not a tenant subdomain, so no
        // middleware has set tenant context yet. Without this, EF's query filter and
        // PostgreSQL RLS both silently treat every read as "no rows" and every write as a
        // rejected WITH CHECK — see IngestActivitySnapshotsCommandHandler for the same pattern.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            ct);

        var tenantId = _device.TenantId;
        var deviceId = _device.DeviceRegistrationId;

        // Idempotent replay: the Tray Service retries on any non-terminal HTTP outcome, and the
        // collector guarantees at most one workflow per attempt id, so a second submit for an
        // attempt id already on file is always a retry, never a genuine conflicting update. 409
        // here (rather than a silent 200) is what ActivitySyncService's IsAttemptAlreadyRecordedAsync
        // checks for to acknowledge and stop retrying without a second upload.
        var existing = await _attempts.GetByIdAsync(tenantId, request.AttemptId, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Inactivity attempt already recorded. AttemptId={AttemptId}", request.AttemptId);
            return Result<Guid>.Conflict("attempt_already_recorded");
        }

        var registeredDevice = await _trayRepo.FindActiveDeviceAsync(deviceId, tenantId, ct);
        var employeeId = registeredDevice?.UserId ?? _device.UserId;

        Guid? evidenceAssetId = null;
        var now = _clock.UtcNow;

        if (request.Outcome == InactivityCaptureOutcomes.Captured)
        {
            var activityEnabled = await _toggles.IsEnabledAsync(
                tenantId, employeeId, MonitoringCapability.ActivityMonitoring, ct);
            var screenshotEnabled = await _toggles.IsEnabledAsync(
                tenantId, employeeId, MonitoringCapability.ScreenshotCapture, ct);
            var autoScreenshotEnabled = await _toggles.IsEnabledAsync(
                tenantId, employeeId, MonitoringCapability.AutoScreenshotCapture, ct);

            if (!activityEnabled || !screenshotEnabled || !autoScreenshotEnabled)
            {
                _logger.LogWarning(
                    "Inactivity capture rejected — policy disabled. AttemptId={AttemptId} TenantId={TenantId}",
                    request.AttemptId, tenantId);
                return Result<Guid>.Forbidden("policy_rejected");
            }

            var idleThresholdMinutes = await _toggles.GetIdleThresholdMinutesAsync(tenantId, employeeId, ct);
            if (request.IdleDurationSeconds < idleThresholdMinutes * 60)
                return Result<Guid>.Failure("idle_too_short", 400);

            var uploadResult = await _fileStorage.UploadAsync(
                tenantId,
                employeeId,
                $"{request.AttemptId:N}.jpg",
                request.ContentType ?? "image/jpeg",
                UploadPurposeCatalog.MonitoringScreenshot,
                request.Content!,
                ct);

            if (!uploadResult.IsSuccess)
                return Result<Guid>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

            var asset = new MonitoringEvidenceAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                AgentDeviceId = deviceId,
                AgentCommandId = null,
                FileRecordId = uploadResult.Value!.Id,
                EvidenceType = "screenshot",
                Source = "agent",
                TriggerType = "inactivity_approved",
                CapturedAt = request.CapturedAt ?? now,
                CreatedAt = now
            };

            _assets.Add(asset);
            evidenceAssetId = asset.Id;
        }

        var attempt = new InactivityCaptureAttempt
        {
            Id = request.AttemptId,
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = deviceId,
            WorkSessionId = null,
            IdleStartedAt = request.IdleStartedAt,
            PromptedAt = request.PromptedAt,
            DecisionAt = request.DecisionAt,
            CapturedAt = request.CapturedAt,
            IdleDurationSeconds = request.IdleDurationSeconds,
            MonitorCount = request.MonitorCount,
            Outcome = request.Outcome,
            FailureCode = request.FailureCode,
            EvidenceAssetId = evidenceAssetId,
            PolicyVersion = request.PolicyVersion,
            CreatedAt = now,
            UpdatedAt = now
        };

        _attempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Inactivity attempt recorded. AttemptId={AttemptId} Outcome={Outcome} DeviceId={DeviceId}",
            attempt.Id, attempt.Outcome, deviceId);

        return Result<Guid>.Success(attempt.Id);
    }
}
