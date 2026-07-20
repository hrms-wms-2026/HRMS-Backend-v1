using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Storage;

namespace ONEVO.Infrastructure.Services.Storage.File;

public sealed class FileStorageService : IFileStorageService
{
    private readonly IFileUploadReservationRepository _reservations;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IStorageQuotaService _quota;
    private readonly IObjectStorageAdapter _objectStorage;
    private readonly IUploadPurposePolicy _purposePolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly FileStorageOptions _options;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(
        IFileUploadReservationRepository reservations,
        IFileRecordRepository fileRecords,
        IStorageQuotaService quota,
        IObjectStorageAdapter objectStorage,
        IUploadPurposePolicy purposePolicy,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        IOptions<FileStorageOptions> options,
        ILogger<FileStorageService> logger)
    {
        _reservations = reservations;
        _fileRecords = fileRecords;
        _quota = quota;
        _objectStorage = objectStorage;
        _purposePolicy = purposePolicy;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<FileUploadReservationDto>> BeginReservationAsync(
        Guid tenantId,
        Guid userId,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        string purpose,
        CancellationToken ct = default)
    {
        // Step 1: purpose-aware validation happens before any quota or DB work.
        var validation = _purposePolicy.ValidateUpload(purpose, originalFileName, contentType, fileSizeBytes);
        if (!validation.IsSuccess)
        {
            return Result<FileUploadReservationDto>.Failure(validation.Error!, validation.StatusCode ?? 400);
        }

        // Step 2: reserve quota bytes atomically before creating any row.
        var reserveResult = await _quota.ReserveStorageAsync(tenantId, fileSizeBytes, ct);
        if (!reserveResult.IsSuccess)
        {
            return Result<FileUploadReservationDto>.Failure(reserveResult.Error!, reserveResult.StatusCode ?? 409);
        }

        // Step 3: generate a fully server-controlled storage key. The client
        // never influences the path beyond the sanitized file name.
        var safeFileName = _purposePolicy.SanitizeFileName(originalFileName);
        var storageKey = _purposePolicy.GenerateStorageKey(tenantId, purpose, safeFileName);
        var now = _clock.UtcNow;

        var reservation = new FileUploadReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReservedBytes = fileSizeBytes,
            Status = FileUploadReservationStatus.Active,
            ReservedByUserId = userId,
            ExpiresAt = now.AddMinutes(_options.ReservationTtlMinutes),
            CompletedFileRecordId = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Step 4: persist the reservation. If this fails, release the bytes we
        // already reserved so they are not stranded.
        try
        {
            await _reservations.AddAsync(reservation, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist file upload reservation for tenant {TenantId}.", tenantId);
            await _quota.ReleaseReservedStorageAsync(tenantId, fileSizeBytes, ct);
            return Result<FileUploadReservationDto>.Failure("file_reservation_failed", 500);
        }

        return Result<FileUploadReservationDto>.Success(new FileUploadReservationDto(
            reservation.Id,
            reservation.TenantId,
            reservation.ReservedBytes,
            reservation.Status,
            storageKey,
            reservation.ExpiresAt,
            reservation.CreatedAt));
    }

    public async Task<Result<FileRecordDto>> CompleteUploadAsync(
        Guid tenantId,
        Guid reservationId,
        string storageKey,
        string originalFileName,
        string safeFileName,
        string contentType,
        string checksumSha256,
        CancellationToken ct = default)
    {
        // Step 1: load the reservation and confirm it is still active.
        var reservation = await _reservations.GetByIdAsync(tenantId, reservationId, ct);
        if (reservation is null)
        {
            return Result<FileRecordDto>.NotFound("Upload reservation not found.");
        }

        if (reservation.Status != FileUploadReservationStatus.Active)
        {
            return Result<FileRecordDto>.Conflict(
                $"Upload reservation is '{reservation.Status}' and cannot be completed.");
        }

        var now = _clock.UtcNow;
        var fileRecord = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = storageKey,
            OriginalFileName = originalFileName,
            SafeFileName = safeFileName,
            ContentType = contentType,
            DetectedContentType = null,
            FileSizeBytes = reservation.ReservedBytes,
            ChecksumSha256 = checksumSha256,
            UploadedByUserId = reservation.ReservedByUserId,
            // No scan pipeline exists in this Phase 1 foundation step. A future
            // scan integration must flip this to Available/Quarantined.
            Status = FileRecordStatus.PendingScan,
            ScanProvider = null,
            ScanResultCode = null,
            ScanCompletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null,
            StorageDeletedAt = null
        };

        // Step 2: atomically flip the reservation Active -> Completed. This is
        // the guard that makes double-complete impossible under concurrency —
        // only one caller's conditional UPDATE can match status = 'active'.
        var transitioned = await _reservations.TryTransitionStatusAsync(
            tenantId, reservationId, FileUploadReservationStatus.Active, FileUploadReservationStatus.Completed,
            fileRecord.Id, ct);

        if (!transitioned)
        {
            return Result<FileRecordDto>.Conflict("Upload reservation was already completed or cancelled.");
        }

        // Step 3: persist the file_records row. If this fails, the object is
        // already sitting in R2 orphaned — roll it back, release the bytes, and
        // put the reservation into Cancelled so it is not left stuck at
        // Completed with no matching file record.
        try
        {
            await _fileRecords.AddAsync(fileRecord, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist file record for tenant {TenantId} after successful upload.", tenantId);

            try
            {
                await _objectStorage.DeleteObjectAsync(storageKey, ct);
            }
            catch (ObjectStorageException deleteEx)
            {
                _logger.LogError(
                    deleteEx,
                    "Failed to roll back orphaned R2 object {StorageKey} for tenant {TenantId}.",
                    storageKey,
                    tenantId);
            }

            await _quota.ReleaseReservedStorageAsync(tenantId, reservation.ReservedBytes, ct);
            await _reservations.TryTransitionStatusAsync(
                tenantId, reservationId, FileUploadReservationStatus.Completed, FileUploadReservationStatus.Cancelled, null, ct);

            return Result<FileRecordDto>.Failure("file_metadata_save_failed", 500);
        }

        // Step 4: move the reserved bytes to used bytes now that both the
        // object and the metadata row are durably saved.
        await _quota.CommitReservedStorageAsync(tenantId, reservation.ReservedBytes, ct);

        return Result<FileRecordDto>.Success(new FileRecordDto(
            fileRecord.Id,
            fileRecord.TenantId,
            fileRecord.StorageKey,
            fileRecord.OriginalFileName,
            fileRecord.SafeFileName,
            fileRecord.ContentType,
            fileRecord.FileSizeBytes,
            fileRecord.ChecksumSha256,
            fileRecord.Status,
            fileRecord.CreatedAt));
    }

    public async Task<Result> CancelReservationAsync(
        Guid tenantId,
        Guid reservationId,
        string reason,
        CancellationToken ct = default)
    {
        var reservation = await _reservations.GetByIdAsync(tenantId, reservationId, ct);
        if (reservation is null)
        {
            return Result.NotFound("Upload reservation not found.");
        }

        // Already cancelled/expired: idempotently safe, no-op success.
        if (reservation.Status == FileUploadReservationStatus.Cancelled
            || reservation.Status == FileUploadReservationStatus.Expired)
        {
            return Result.Success();
        }

        // Already completed: cancelling after completion is rejected, not
        // silently accepted, since bytes have already moved to used.
        if (reservation.Status == FileUploadReservationStatus.Completed)
        {
            return Result.Conflict("Upload reservation was already completed and cannot be cancelled.");
        }

        var transitioned = await _reservations.TryTransitionStatusAsync(
            tenantId, reservationId, FileUploadReservationStatus.Active, FileUploadReservationStatus.Cancelled, null, ct);

        if (!transitioned)
        {
            // Lost a race with a concurrent complete/cancel call — the other
            // caller's outcome stands; treat this call as a safe no-op.
            return Result.Success();
        }

        await _quota.ReleaseReservedStorageAsync(tenantId, reservation.ReservedBytes, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<FileRecordDto>> UploadAsync(
        Guid tenantId,
        Guid userId,
        string originalFileName,
        string contentType,
        string purpose,
        Stream content,
        CancellationToken ct = default)
    {
        if (content is null || content.Length <= 0)
        {
            return Result<FileRecordDto>.Failure("Upload content is empty.", 400);
        }

        // Step 1: begin the reservation (validates purpose + reserves quota).
        var beginResult = await BeginReservationAsync(
            tenantId, userId, originalFileName, contentType, content.Length, purpose, ct);

        if (!beginResult.IsSuccess)
        {
            return Result<FileRecordDto>.Failure(beginResult.Error!, beginResult.StatusCode ?? 400);
        }

        var reservation = beginResult.Value!;

        // Step 2: buffer the content once so we can both hash it and upload it
        // without requiring the caller's stream to be re-readable.
        using var buffer = new MemoryStream();
        content.Position = 0;
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        string checksum;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(buffer, ct);
            checksum = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        buffer.Position = 0;

        // Step 3: upload to R2. Any failure releases the reservation and
        // returns without ever creating a file_records row.
        try
        {
            await _objectStorage.PutObjectAsync(reservation.StorageKey, buffer, contentType, ct);
        }
        catch (ObjectStorageException ex)
        {
            _logger.LogError(
                ex,
                "R2 upload failed for tenant {TenantId}, reservation {ReservationId}.",
                tenantId,
                reservation.Id);

            await CancelReservationAsync(tenantId, reservation.Id, "object_storage_upload_failed", ct);
            return Result<FileRecordDto>.Failure("file_upload_failed", 502);
        }

        // Step 4: complete the reservation now that the object is durably in R2.
        var safeFileName = _purposePolicy.SanitizeFileName(originalFileName);

        return await CompleteUploadAsync(
            tenantId, reservation.Id, reservation.StorageKey, originalFileName, safeFileName, contentType, checksum, ct);
    }
}
