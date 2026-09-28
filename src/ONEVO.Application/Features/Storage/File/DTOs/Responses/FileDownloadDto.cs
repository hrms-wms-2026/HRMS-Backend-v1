namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

public sealed record FileDownloadDto(
    Stream? Content,
    string ContentType,
    long ContentLength,
    string? ETag,
    bool IsPrivateCacheableAvatar,
    bool NotModified);
