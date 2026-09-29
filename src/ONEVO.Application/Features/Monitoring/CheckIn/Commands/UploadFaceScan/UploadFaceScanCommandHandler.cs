using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ExceptionStatus = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionStatus;
using ExceptionType = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionType;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
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
    private readonly IBiometricProfileRepository _profiles;
    private readonly IEnrolledFaceMatcher _matcher;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExceptionRepository _exceptions;
    private readonly IExceptionAlertRouterFactory _alertRouters;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ILogger<UploadFaceScanCommandHandler> _logger;

    public UploadFaceScanCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IFileStorageService fileStorage,
        IBiometricProfileRepository profiles,
        IEnrolledFaceMatcher matcher,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        IExceptionRepository exceptions,
        IExceptionAlertRouterFactory alertRouters,
        ILegalEntityRepository legalEntities,
        ILogger<UploadFaceScanCommandHandler>? logger = null)
    {
        _exceptions = exceptions;
        _alertRouters = alertRouters;
        _legalEntities = legalEntities;
        _logger = logger ?? NullLogger<UploadFaceScanCommandHandler>.Instance;
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _fileStorage = fileStorage;
        _profiles = profiles;
        _matcher = matcher;
        _employeeIdentity = employeeIdentity;
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

        // Must match whatever CompleteEnrollmentAttemptCommandHandler resolved when it stored the
        // BiometricProfile - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            _device.TenantId, _device.UserId, _device.LegalEntityId, cancellationToken);
        var (matchStatus, similarity) = await VerifyAgainstReferencePhotoAsync(employeeId, fileRecord.Id, cancellationToken);

        var now = _clock.UtcNow;
        var faceScan = new MonitoringFaceScan
        {
            Id              = Guid.NewGuid(),
            TenantId        = _device.TenantId,
            CheckInId       = request.CheckInId,
            StorageKey      = fileRecord.StorageKey,
            FileSizeBytes   = request.FileSizeBytes,
            ContentType     = request.ContentType,
            Status          = matchStatus,
            SimilarityScore = similarity,
            CreatedAt       = now,
            UpdatedAt       = null
        };

        await _repository.AddFaceScanAsync(faceScan, cancellationToken);

        checkIn.FaceScanId = faceScan.Id;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (matchStatus == MonitoringFaceScanStatus.NotMatched && employeeId != Guid.Empty)
            await RaiseIdentityCaseAsync(employeeId, checkIn, faceScan, fileRecord.Id, cancellationToken);

        return Result<FaceScanUploadResponseDto>.Success(new FaceScanUploadResponseDto(
            faceScan.Id,
            faceScan.Status,
            faceScan.FileSizeBytes,
            faceScan.SimilarityScore));
    }

    /// <summary>
    /// The person at the laptop is not the enrolled employee: open an identity case and alert the
    /// reporting manager (or HR). The scan is already saved, and the tray never retries a failed
    /// upload, so nothing here may fail the request. The tray can upload photos repeatedly, so
    /// while an identity case for this employee is still open from the same company day, later
    /// scans add no new case and no new alert; a mismatch on a new day opens a new case.
    /// </summary>
    private async Task RaiseIdentityCaseAsync(
        Guid employeeId, EmployeeCheckIn checkIn, MonitoringFaceScan faceScan, Guid photoFileId, CancellationToken ct)
    {
        MonitoringException identityCase;
        try
        {
            var legalEntity = _device.LegalEntityId is Guid legalEntityId
                ? await _legalEntities.GetByIdForTenantAsync(_device.TenantId, legalEntityId, ct)
                : null;
            var dayStart = CompanyTimeZone.StartOfDay(faceScan.CreatedAt, CompanyTimeZone.Find(legalEntity?.Timezone));
            if (await _exceptions.HasUnresolvedSinceAsync(
                    _device.TenantId, employeeId, ExceptionType.IdentityAnomaly, dayStart, ct))
                return;

            var score = faceScan.SimilarityScore is float s ? $" (similarity {s:0.#}%)" : string.Empty;
            identityCase = new MonitoringException
            {
                Id = Guid.NewGuid(),
                TenantId = _device.TenantId,
                EmployeeId = employeeId,
                Type = ExceptionType.IdentityAnomaly,
                Status = ExceptionStatus.Open,
                Title = "Face did not match",
                Description = $"A check-in photo did not match the employee's enrolled face{score}.",
                DetectedAt = faceScan.CreatedAt,
                MetadataJson = new ExceptionMetadata
                {
                    Source = ExceptionMetadata.SourceCheckInScan,
                    OccurredAt = checkIn.CheckedInAt,
                    FaceScanId = faceScan.Id,
                    CheckInId = checkIn.Id,
                    SimilarityScore = faceScan.SimilarityScore,
                    Reasons = ["not_matched"],
                    DeviceRegistrationId = checkIn.DeviceRegistrationId,
                    PhotoFileId = photoFileId
                }.ToJson()
            };

            // Saved on its own first, so a failure working out who to alert can't lose the case.
            await _exceptions.AddAsync(identityCase, ct);
            await _exceptions.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Face scan {FaceScanId} did not match employee {EmployeeId}, but the identity case could not be opened",
                faceScan.Id, employeeId);
            return;
        }

        try
        {
            // A tray request has no signed-in web user, so the router is pinned to the tenant.
            await _alertRouters.CreateForTenant(_device.TenantId).NotifyDetectedAsync(identityCase, ct);
            await _exceptions.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Identity case {ExceptionId} for employee {EmployeeId} was opened but the reviewer could not be alerted",
                identityCase.Id, employeeId);
        }
    }

    private async Task<(string Status, float? Similarity)> VerifyAgainstReferencePhotoAsync(
        Guid employeeId, Guid capturedFileId, CancellationToken ct)
    {
        var profile = await _profiles.GetByEmployeeIdAsync(_device.TenantId, employeeId, ct);
        if (profile?.ReferencePhotoFileId is null
            && profile?.LeftReferencePhotoFileId is null
            && profile?.RightReferencePhotoFileId is null)
        {
            return (MonitoringFaceScanStatus.NoReferencePhoto, null);
        }

        try
        {
            var capturedRead = await _fileStorage.OpenReadAsync(_device.TenantId, capturedFileId, ct);
            if (!capturedRead.IsSuccess)
                return (MonitoringFaceScanStatus.Failed, null);

            await using var capturedStream = capturedRead.Value!.Content;
            var match = await _matcher.MatchAsync(_device.TenantId, profile, capturedStream, ct);

            if (!match.HasReference)
                return (MonitoringFaceScanStatus.NoReferencePhoto, null);
            if (match.Failed)
                return (MonitoringFaceScanStatus.Failed, null);

            return match.IsMatch
                ? (MonitoringFaceScanStatus.Verified, match.Similarity)
                : (MonitoringFaceScanStatus.NotMatched, match.Similarity);
        }
        catch (Exception)
        {
            return (MonitoringFaceScanStatus.Failed, null);
        }
    }
}
