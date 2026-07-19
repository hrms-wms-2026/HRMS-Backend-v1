using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

/// <summary>
/// The single reusable entry point every upload feature must call. No feature
/// handler may reserve quota, call IObjectStorageAdapter, or write file_records
/// / file_upload_reservations directly.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Validates the upload against its purpose, reserves quota bytes, and
    /// creates an active file_upload_reservations row. Does not touch object
    /// storage.
    /// </summary>
    Task<Result<FileUploadReservationDto>> BeginReservationAsync(
        Guid tenantId,
        Guid userId,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        string purpose,
        CancellationToken ct = default);

    /// <summary>
    /// Completes an active reservation after the object bytes are already in R2:
    /// creates the file_records row, links it back to the reservation, and moves
    /// reserved bytes to used bytes. Rejects (409) an already-completed or
    /// already-cancelled reservation.
    /// </summary>
    Task<Result<FileRecordDto>> CompleteUploadAsync(
        Guid tenantId,
        Guid reservationId,
        string storageKey,
        string originalFileName,
        string safeFileName,
        string contentType,
        string checksumSha256,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels an active reservation and releases its reserved bytes.
    /// Idempotently safe when the reservation is already cancelled/expired;
    /// returns 409 when it is already completed.
    /// </summary>
    Task<Result> CancelReservationAsync(
        Guid tenantId,
        Guid reservationId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Single orchestrated call for server-mediated uploads: begins the
    /// reservation, uploads to R2, and completes it — releasing the reservation
    /// on any failure. This is the method most future feature handlers should
    /// call. <paramref name="content"/> must support <c>Length</c> (a buffered
    /// or file-backed stream, not a raw non-seekable network stream).
    /// </summary>
    Task<Result<FileRecordDto>> UploadAsync(
        Guid tenantId,
        Guid userId,
        string originalFileName,
        string contentType,
        string purpose,
        Stream content,
        CancellationToken ct = default);
}
