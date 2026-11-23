using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

/// <summary>A re-encoded image. The caller owns <see cref="Content"/>.</summary>
public sealed record SanitizedImage(Stream Content, string ContentType);

/// <summary>
/// Task 23: decodes an uploaded raster image and encodes it again from its pixels only, in the same
/// format. That drops EXIF (including GPS position), XMP, ICC comments and any bytes hidden after
/// the image data, and bakes the EXIF orientation into the pixels so photos stay upright.
/// FileStorageService calls it for every PNG, JPEG and WebP upload; avatars use
/// <see cref="IAvatarImageProcessor"/>, which re-encodes too.
/// </summary>
public interface IImageSanitizer
{
    /// <summary>True for the file types this sanitizer re-encodes (.png, .jpg, .jpeg, .webp).</summary>
    bool CanSanitize(string fileName);

    /// <summary>Returns 400 when the bytes are not a decodable image or are too large to decode safely.</summary>
    Task<Result<SanitizedImage>> SanitizeAsync(string fileName, Stream content, CancellationToken ct = default);
}
