using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;

public class UploadFaceScanCommandHandler
    : IRequestHandler<UploadFaceScanCommand, Result<FaceScanUploadResponseDto>>
{
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UploadFaceScanCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _fileStorage = fileStorage;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<FaceScanUploadResponseDto>> Handle(
        UploadFaceScanCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<FaceScanUploadResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<FaceScanUploadResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var checkIn = await _repository.FindCheckInAsync(
            request.CheckInId, _device.TenantId, cancellationToken);

        if (checkIn is null)
            return Result<FaceScanUploadResponseDto>.NotFound("Check-in not found.");

        if (checkIn.UserId != _device.UserId)
            return Result<FaceScanUploadResponseDto>.Forbidden();

        var ext = request.ContentType switch
        {
            "image/png"  => "png",
            "image/webp" => "webp",
            _            => "jpg"
        };
        var originalFileName = $"scan.{ext}";

        var uploadResult = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            originalFileName,
            request.ContentType,
            UploadPurposeCatalog.MonitoringFaceScan,
            request.ImageStream,
            cancellationToken);

        if (!uploadResult.IsSuccess)
        {
            return Result<FaceScanUploadResponseDto>.Failure(
                uploadResult.Error!, uploadResult.StatusCode ?? 500);
        }

        var fileRecord = uploadResult.Value!;

        var now = _clock.UtcNow;
        var faceScan = new MonitoringFaceScan
        {
            Id            = Guid.NewGuid(),
            TenantId      = _device.TenantId,
            CheckInId     = request.CheckInId,
            StorageKey    = fileRecord.StorageKey,
            FileSizeBytes = request.FileSizeBytes,
            ContentType   = request.ContentType,
            Status        = MonitoringFaceScanStatus.Available,
            CreatedAt     = now,
            UpdatedAt     = null
        };

        await _repository.AddFaceScanAsync(faceScan, cancellationToken);

        checkIn.FaceScanId = faceScan.Id;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FaceScanUploadResponseDto>.Success(new FaceScanUploadResponseDto(
            faceScan.Id,
            faceScan.Status,
            faceScan.FileSizeBytes));
    }
}
