using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Services;

/// <summary>Who is uploading: exactly one of the two ids is set.</summary>
public sealed record SupportUploader(Guid? PlatformUserId, Guid? TenantUserId, string Name);

public sealed record SupportUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Support attachments bypass IFileStorageService on purpose: file_records is TenantStrict and keyed to
/// tenant users, so platform staff could neither write nor read there. Files are validated with the same
/// upload purpose policy, stored under support-tickets/{tenant}/{ticket}/{attachment}/, and are not counted
/// against the tenant's storage quota.
/// </summary>
public interface ISupportAttachmentService
{
    Task<Result<SupportTicketAttachment>> StoreAsync(
        SupportTicket ticket, SupportUpload upload, SupportUploader uploader, bool isInternal, CancellationToken ct);

    /// <summary>Best effort: used to clean up an object whose database row failed to save.</summary>
    Task DeleteQuietlyAsync(string objectKey, CancellationToken ct);

    Task<SupportAttachmentDownloadDto> GetDownloadUrlAsync(SupportTicketAttachment attachment, CancellationToken ct);
}

public sealed class SupportAttachmentService : ISupportAttachmentService
{
    private readonly IUploadPurposePolicy _policy;
    private readonly IObjectStorageAdapter _storage;
    private readonly IDateTimeProvider _clock;
    private readonly IImageSanitizer _imageSanitizer;

    public SupportAttachmentService(
        IUploadPurposePolicy policy, IObjectStorageAdapter storage, IDateTimeProvider clock, IImageSanitizer imageSanitizer)
    {
        _policy = policy;
        _storage = storage;
        _clock = clock;
        _imageSanitizer = imageSanitizer;
    }

    public async Task<Result<SupportTicketAttachment>> StoreAsync(
        SupportTicket ticket, SupportUpload upload, SupportUploader uploader, bool isInternal, CancellationToken ct)
    {
        var contentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType;

        var valid = _policy.ValidateUpload(
            UploadPurposeCatalog.SupportTicketAttachment, upload.FileName, contentType, upload.SizeBytes);
        if (!valid.IsSuccess)
        {
            return Result<SupportTicketAttachment>.Failure(valid.Error!, valid.StatusCode ?? 400, valid.ErrorCode);
        }

        var signature = _policy.ValidateSignature(upload.FileName, upload.Content);
        if (!signature.IsSuccess)
        {
            return Result<SupportTicketAttachment>.Failure(signature.Error!, signature.StatusCode ?? 400, signature.ErrorCode);
        }

        // Task 23: screenshots attached to tickets are re-encoded like every other image upload,
        // so EXIF/GPS and hidden trailing bytes never reach storage.
        var content = upload.Content;
        var sizeBytes = upload.SizeBytes;
        SanitizedImage? sanitized = null;
        if (_imageSanitizer.CanSanitize(upload.FileName))
        {
            var result = await _imageSanitizer.SanitizeAsync(upload.FileName, upload.Content, ct);
            if (!result.IsSuccess)
            {
                return Result<SupportTicketAttachment>.Failure(result.Error!, result.StatusCode ?? 400, result.ErrorCode);
            }

            sanitized = result.Value!;
            content = sanitized.Content;
            contentType = sanitized.ContentType;
            sizeBytes = sanitized.Content.Length;
        }

        try
        {
            var safeName = _policy.SanitizeFileName(upload.FileName);
            var id = Guid.NewGuid();
            // Task 23: the object name is server-generated; the client's (sanitized) name is display-only.
            var key = $"support-tickets/{ticket.TenantId:N}/{ticket.Id:N}/{id:N}/{ServerObjectName(safeName)}";

            await _storage.PutObjectAsync(key, content, contentType, ct);

            return Result<SupportTicketAttachment>.Success(new SupportTicketAttachment
            {
                Id = id,
                TenantId = ticket.TenantId,
                TicketId = ticket.Id,
                FileName = safeName,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                ObjectKey = key,
                IsInternal = isInternal,
                UploadedByPlatformUserId = uploader.PlatformUserId,
                UploadedByUserId = uploader.TenantUserId,
                UploadedByName = uploader.Name,
                CreatedAt = _clock.UtcNow,
            });
        }
        finally
        {
            if (sanitized is not null)
                await sanitized.Content.DisposeAsync();
        }
    }

    /// <summary>"content" plus the extension of an already sanitized name (safe charset only).</summary>
    private static string ServerObjectName(string safeFileName) =>
        "content" + Path.GetExtension(safeFileName).ToLowerInvariant();

    public async Task DeleteQuietlyAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteObjectAsync(objectKey, ct);
        }
        catch (Exception)
        {
            // An orphaned object is harmless: nothing references it and its key is unguessable.
        }
    }

    public async Task<SupportAttachmentDownloadDto> GetDownloadUrlAsync(
        SupportTicketAttachment attachment, CancellationToken ct)
    {
        var url = await _storage.GetSignedUrlAsync(attachment.ObjectKey, SupportTicketRules.DownloadUrlLifetime, ct);
        return new SupportAttachmentDownloadDto(url, _clock.UtcNow.Add(SupportTicketRules.DownloadUrlLifetime));
    }
}
