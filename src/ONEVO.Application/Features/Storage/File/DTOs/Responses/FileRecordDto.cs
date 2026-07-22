namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

public sealed record FileRecordDto(
    Guid Id,
    Guid TenantId,
    string OriginalFileName,
    string SafeFileName,
    string ContentType,
    long FileSizeBytes,
    string ChecksumSha256,
    string Status,
    DateTimeOffset CreatedAt);
