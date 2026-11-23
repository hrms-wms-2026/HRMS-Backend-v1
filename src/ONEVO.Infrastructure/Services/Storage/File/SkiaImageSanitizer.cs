using SkiaSharp;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Storage.File;

/// <summary>
/// Task 23: re-encodes PNG, JPEG and WebP uploads with SkiaSharp. Only the decoded pixels are
/// written back, so EXIF/GPS, XMP, comments and anything appended after the image data are gone.
/// The format is kept (PNG stays lossless; JPEG and WebP use quality 90), so stored files keep the
/// extension and content type they were validated with.
///
/// Decompression-bomb guard: the header is read first and anything over <see cref="MaxDimension"/>
/// on a side or <see cref="MaxPixelCount"/> pixels is refused before the pixels are allocated.
/// The limits fit a screenshot of three 4K monitors side by side.
/// </summary>
public sealed class SkiaImageSanitizer : IImageSanitizer
{
    public const int MaxDimension = 16_384;
    public const long MaxPixelCount = 40_000_000;
    public const int LossyQuality = 90;

    public bool CanSanitize(string fileName) => FormatFor(fileName) is not null;

    public async Task<Result<SanitizedImage>> SanitizeAsync(
        string fileName, Stream content, CancellationToken ct = default)
    {
        var format = FormatFor(fileName);
        if (format is null)
            return Result<SanitizedImage>.Failure($"'{Path.GetExtension(fileName)}' images cannot be re-encoded.", 400);

        try
        {
            using var source = new MemoryStream();
            if (content.CanSeek)
                content.Position = 0;
            await content.CopyToAsync(source, ct);
            ct.ThrowIfCancellationRequested();

            using var encoded = SKData.CreateCopy(source.ToArray());
            using var codec = SKCodec.Create(encoded);
            if (codec is null || codec.EncodedFormat != format.Value.Decoded)
                return Invalid();

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0
                || info.Width > MaxDimension || info.Height > MaxDimension
                || (long)info.Width * info.Height > MaxPixelCount)
            {
                return Result<SanitizedImage>.Failure(
                    $"Image dimensions exceed the supported limit of {MaxPixelCount} pixels " +
                    $"or {MaxDimension} pixels on a side.", 400);
            }

            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return Invalid();

            using var oriented = codec.EncodedOrigin == SKEncodedOrigin.TopLeft
                ? null
                : SkiaImageOrientation.Apply(decoded, codec.EncodedOrigin);
            using var image = SKImage.FromBitmap(oriented ?? decoded);
            using var output = image.Encode(format.Value.Encoded, format.Value.Quality);
            if (output is null)
                return Invalid();

            return Result<SanitizedImage>.Success(new SanitizedImage(
                new MemoryStream(output.ToArray(), writable: false), format.Value.ContentType));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    private static Result<SanitizedImage> Invalid() =>
        Result<SanitizedImage>.Failure("The uploaded file is not a valid image.", 400);

    private readonly record struct ImageFormat(
        SKEncodedImageFormat Decoded, SKEncodedImageFormat Encoded, int Quality, string ContentType);

    private static ImageFormat? FormatFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => new ImageFormat(SKEncodedImageFormat.Png, SKEncodedImageFormat.Png, 100, "image/png"),
            ".jpg" or ".jpeg" => new ImageFormat(SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Jpeg, LossyQuality, "image/jpeg"),
            ".webp" => new ImageFormat(SKEncodedImageFormat.Webp, SKEncodedImageFormat.Webp, LossyQuality, "image/webp"),
            _ => null
        };
}
