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
        string purpose,
        string originalFileName,
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

    /// <summary>
    /// Returns a time-limited pre-signed URL granting read access to the stored file.
    /// Feature handlers must call this instead of using IObjectStorageAdapter directly.
    /// Returns 404 when the file record does not exist within the tenant.
    /// </summary>
    Task<Result<string>> GetSignedUrlAsync(
        Guid tenantId,
        Guid fileRecordId,
        TimeSpan expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a readable stream for a file this tenant already legitimately
    /// owns (e.g. one referenced by a domain entity's own FileId column).
    /// This is not a lookup for validating untrusted, client-supplied file
    /// ids - callers must already know the id is legitimately theirs before
    /// calling it. The tenant filter here is a second, defensive check, not
    /// the primary trust boundary.
    /// </summary>
    Task<Result<FileStreamDto>> OpenReadAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default);

    /// <summary>
    /// Looks up a file record's metadata (including <see cref="FileRecordDto.UploadedByUserId"/>
    /// and <see cref="FileRecordDto.DeletedAt"/>) for a caller that needs to validate ownership
    /// or existence of an untrusted, client-supplied file id before deciding whether to link,
    /// read, or delete it - e.g. confirming a file was uploaded by the current user before
    /// attaching it to a task. This is the sanctioned replacement for querying
    /// IFileRecordRepository directly; no other feature code may do that. Returns 404 when the
    /// file record does not exist within the tenant. Interpreting DeletedAt/UploadedByUserId is
    /// the caller's responsibility, same trust model as <see cref="OpenReadAsync"/>.
    /// </summary>
    Task<Result<FileRecordDto>> GetRecordAsync(
        Guid tenantId,
        Guid fileRecordId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a file this tenant owns: marks the file_records row
    /// deleted, best-effort deletes the underlying object, and releases the
    /// bytes it was consuming back to the tenant's used-storage quota.
    /// Idempotent — deleting an already-deleted record is a no-op success.
    /// Caller-ownership (e.g. "only the uploader may delete") is the calling
    /// feature handler's responsibility, not this method's — same trust model
    /// documented on <see cref="OpenReadAsync"/>.
    /// </summary>
    Task<Result> DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default);
}
