using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;

public class SubmitPeriodicScreenshotCommandHandler
    : IRequestHandler<SubmitPeriodicScreenshotCommand, Result<Guid>>
{
    private readonly IFileStorageService _fileStorage;
    private readonly IEvidenceAssetRepository _assets;
    private readonly ITrayActivationRepository _trayRepo;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitPeriodicScreenshotCommandHandler> _logger;

    public SubmitPeriodicScreenshotCommandHandler(
        IFileStorageService fileStorage,
        IEvidenceAssetRepository assets,
        ITrayActivationRepository trayRepo,
        ITrayCurrentDevice device,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<SubmitPeriodicScreenshotCommandHandler> logger)
    {
        _fileStorage = fileStorage;
        _assets = assets;
        _trayRepo = trayRepo;
        _device = device;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(SubmitPeriodicScreenshotCommand request, CancellationToken ct)
    {
        var tenantId = _device.TenantId;
        var deviceId = _device.DeviceRegistrationId;

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId,
            _device.UserId,
            request.FileName,
            request.ContentType,
            UploadPurposeCatalog.MonitoringScreenshot,
            request.Content,
            ct);

        if (!uploadResult.IsSuccess)
            return Result<Guid>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        // Phase 1: UserId on TrayDeviceRegistration serves as employeeId
        var registeredDevice = await _trayRepo.FindActiveDeviceAsync(deviceId, tenantId, ct);
        var employeeId = registeredDevice?.UserId ?? _device.UserId;

        var now = _clock.UtcNow;
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
            TriggerType = "periodic",
            CapturedAt = request.CapturedAt,
            CreatedAt = now
        };

        _assets.Add(asset);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Periodic screenshot recorded. AssetId={AssetId} DeviceId={DeviceId}",
            asset.Id, deviceId);

        return Result<Guid>.Success(asset.Id);
    }
}
