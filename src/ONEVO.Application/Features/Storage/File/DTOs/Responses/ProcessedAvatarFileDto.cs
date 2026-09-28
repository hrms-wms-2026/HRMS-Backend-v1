namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

/// <summary>
/// A normalized avatar stream ready for durable storage. The caller owns and must
/// dispose <see cref="Content"/>.
/// </summary>
public sealed record ProcessedAvatarFileDto(
    Stream Content,
    string StorageFileName,
    string ContentType);
