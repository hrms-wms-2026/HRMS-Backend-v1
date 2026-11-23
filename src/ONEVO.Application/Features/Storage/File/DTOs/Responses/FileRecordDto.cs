using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

/// <summary>File metadata. <see cref="TenantId"/> and <see cref="UploadedByUserId"/> stay on the record for
/// server-side ownership checks but are never serialized: they are internal ids (task 44,
/// docs/security/data-classification.md).</summary>
public sealed record FileRecordDto(
    Guid Id,
    [property: JsonIgnore] Guid TenantId,
    string StorageKey,
    string OriginalFileName,
    string SafeFileName,
    string ContentType,
    long FileSizeBytes,
    string ChecksumSha256,
    string Status,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore] Guid UploadedByUserId,
    DateTimeOffset? DeletedAt);
