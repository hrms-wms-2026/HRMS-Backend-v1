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

    public SupportAttachmentService(IUploadPurposePolicy policy, IObjectStorageAdapter storage, IDateTimeProvider clock)
    {
        _policy = policy;
        _storage = storage;
        _clock = clock;
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

        var safeName = _policy.SanitizeFileName(upload.FileName);
        var id = Guid.NewGuid();
        var key = $"support-tickets/{ticket.TenantId:N}/{ticket.Id:N}/{id:N}/{safeName}";

        await _storage.PutObjectAsync(key, upload.Content, contentType, ct);

        return Result<SupportTicketAttachment>.Success(new SupportTicketAttachment
        {
            Id = id,
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            FileName = safeName,
            ContentType = contentType,
            SizeBytes = upload.SizeBytes,
            ObjectKey = key,
            IsInternal = isInternal,
            UploadedByPlatformUserId = uploader.PlatformUserId,
            UploadedByUserId = uploader.TenantUserId,
            UploadedByName = uploader.Name,
            CreatedAt = _clock.UtcNow,
        });
    }

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
