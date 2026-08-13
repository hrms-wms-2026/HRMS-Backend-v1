using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;

public sealed class SubmitInactivityCaptureAttemptCommandHandler
    : IRequestHandler<SubmitInactivityCaptureAttemptCommand, Result<SubmitInactivityCaptureAttemptResponse>>
{
    public const string AttemptAlreadyRecordedCode = "attempt_already_recorded";

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IFileStorageService _fileStorage;
    private readonly IEvidenceAssetRepository _assets;
    private readonly IInactivityCaptureAttemptRepository _attempts;
    private readonly ITrayActivationRepository _trayRepo;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitInactivityCaptureAttemptCommandHandler> _logger;

    public SubmitInactivityCaptureAttemptCommandHandler(
        IFileStorageService fileStorage,
        IEvidenceAssetRepository assets,
        IInactivityCaptureAttemptRepository attempts,
        ITrayActivationRepository trayRepo,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<SubmitInactivityCaptureAttemptCommandHandler> logger)
    {
        _fileStorage = fileStorage;
        _assets = assets;
        _attempts = attempts;
        _trayRepo = trayRepo;
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<SubmitInactivityCaptureAttemptResponse>> Handle(
        SubmitInactivityCaptureAttemptCommand request,
        CancellationToken ct)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<SubmitInactivityCaptureAttemptResponse>.Failure(
                "A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, ct);
        if (tenant is null)
            return Result<SubmitInactivityCaptureAttemptResponse>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            ct);

        var tenantId = _device.TenantId;
        var deviceId = _device.DeviceRegistrationId;

        var registeredDevice = await _trayRepo.FindActiveDeviceAsync(deviceId, tenantId, ct);
        var employeeId = registeredDevice?.UserId ?? _device.UserId;

        var existing = await _attempts.GetByIdAsync(tenantId, request.AttemptId, ct);
        if (existing is not null)
        {
            if (!IsEquivalent(existing, request))
            {
                return Result<SubmitInactivityCaptureAttemptResponse>.Failure(
                    AttemptAlreadyRecordedCode, 409);
            }

            return Result<SubmitInactivityCaptureAttemptResponse>.Success(
                await BuildResponseAsync(tenantId, existing, ct));
        }

        if (request.Outcome == InactivityCaptureOutcomes.Captured)
        {
            var captureEnabled = await IsInactivityCaptureEnabledAsync(tenantId, employeeId, ct);
            if (!captureEnabled)
            {
                return Result<SubmitInactivityCaptureAttemptResponse>.Failure(
                    MonitoringErrors.ScreenshotCapabilityDisabled, 403);
            }
        }

        Guid? evidenceAssetId = null;
        Guid? fileRecordId = null;
        var now = _clock.UtcNow;

        if (request.Outcome == InactivityCaptureOutcomes.Captured)
        {
            var uploadResult = await _fileStorage.UploadAsync(
                tenantId,
                employeeId,
                request.FileName ?? "inactivity.jpg",
                request.ContentType!,
                UploadPurposeCatalog.MonitoringScreenshot,
                request.Content!,
                ct);

            if (!uploadResult.IsSuccess)
            {
                return Result<SubmitInactivityCaptureAttemptResponse>.Failure(
                    uploadResult.Error!, uploadResult.StatusCode ?? 400);
            }

            fileRecordId = uploadResult.Value!.Id;

            var asset = new MonitoringEvidenceAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                AgentDeviceId = deviceId,
                AgentCommandId = null,
                FileRecordId = fileRecordId.Value,
                EvidenceType = "screenshot",
                Source = "agent",
                TriggerType = "inactivity_approved",
                MetadataJson = BuildEvidenceMetadataJson(request),
                CapturedAt = request.CapturedAt!.Value,
                CreatedAt = now
            };

            _assets.Add(asset);
            evidenceAssetId = asset.Id;
        }

        var workSessionId = await _attempts.FindContainingWorkSessionAsync(
            tenantId, employeeId, request.PromptedAt, ct);

        var attempt = new InactivityCaptureAttempt
        {
            Id = request.AttemptId,
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = deviceId,
            WorkSessionId = workSessionId,
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

        await _attempts.AddAsync(attempt, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Inactivity capture attempt recorded. AttemptId={AttemptId} Outcome={Outcome} DeviceId={DeviceId}",
            request.AttemptId, request.Outcome, deviceId);

        return Result<SubmitInactivityCaptureAttemptResponse>.Success(
            new SubmitInactivityCaptureAttemptResponse(request.AttemptId, evidenceAssetId, fileRecordId));
    }

    private async Task<bool> IsInactivityCaptureEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct)
    {
        var activity = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ActivityMonitoring, ct);
        var screenshot = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ScreenshotCapture, ct);
        var autoScreenshot = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.AutoScreenshotCapture, ct);

        return activity && screenshot && autoScreenshot;
    }

    private async Task<SubmitInactivityCaptureAttemptResponse> BuildResponseAsync(
        Guid tenantId,
        InactivityCaptureAttempt attempt,
        CancellationToken ct)
    {
        Guid? fileRecordId = null;
        if (attempt.EvidenceAssetId.HasValue)
        {
            var asset = await _assets.GetByIdAsync(tenantId, attempt.EvidenceAssetId.Value, ct);
            fileRecordId = asset?.FileRecordId;
        }

        return new SubmitInactivityCaptureAttemptResponse(
            attempt.Id,
            attempt.EvidenceAssetId,
            fileRecordId);
    }

    private static bool IsEquivalent(
        InactivityCaptureAttempt existing,
        SubmitInactivityCaptureAttemptCommand request)
        => existing.Outcome == request.Outcome
           && existing.PolicyVersion == request.PolicyVersion
           && existing.IdleStartedAt == request.IdleStartedAt
           && existing.PromptedAt == request.PromptedAt
           && existing.DecisionAt == request.DecisionAt
           && existing.CapturedAt == request.CapturedAt
           && existing.IdleDurationSeconds == request.IdleDurationSeconds
           && existing.MonitorCount == request.MonitorCount
           && existing.FailureCode == request.FailureCode;

    private static string BuildEvidenceMetadataJson(SubmitInactivityCaptureAttemptCommand request)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["inactivity_attempt_id"] = request.AttemptId,
            ["monitor_count"] = request.MonitorCount,
            ["sha256"] = request.Sha256,
            ["encoded_byte_count"] = request.FileSizeBytes
        };

        if (request.VirtualBoundsX.HasValue
            && request.VirtualBoundsY.HasValue
            && request.VirtualBoundsWidth.HasValue
            && request.VirtualBoundsHeight.HasValue)
        {
            metadata["virtual_bounds"] = new
            {
                x = request.VirtualBoundsX.Value,
                y = request.VirtualBoundsY.Value,
                width = request.VirtualBoundsWidth.Value,
                height = request.VirtualBoundsHeight.Value
            };
        }

        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }
}
