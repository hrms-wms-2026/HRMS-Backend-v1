using SkiaSharp;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Storage.File;

public sealed class AvatarImageProcessor : IAvatarImageProcessor
{
    public const int AvatarSize = 256;
    public const int MaxDimension = 4_096;
    public const long MaxPixelCount = 16_000_000;
    private const int WebpQuality = 82;

    public async Task<Result<ProcessedAvatarFileDto>> ProcessAsync(
        string originalFileName,
        Stream content,
        CancellationToken ct = default)
    {
        if (content is null || !content.CanRead)
        {
            return Result<ProcessedAvatarFileDto>.Failure("Avatar content is unreadable.", 400);
        }

        try
        {
            using var source = new MemoryStream();
            if (content.CanSeek)
                content.Position = 0;

            await content.CopyToAsync(source, ct);
            ct.ThrowIfCancellationRequested();

            using var encodedData = SKData.CreateCopy(source.ToArray());
            using var codec = SKCodec.Create(encodedData);
            if (codec is null)
            {
                return Result<ProcessedAvatarFileDto>.Failure("The uploaded avatar is not a valid image.", 400);
            }

            var imageInfo = codec.Info;
            var pixelCount = (long)imageInfo.Width * imageInfo.Height;
            if (imageInfo.Width <= 0 || imageInfo.Height <= 0 ||
                imageInfo.Width > MaxDimension || imageInfo.Height > MaxDimension ||
                pixelCount > MaxPixelCount)
            {
                return Result<ProcessedAvatarFileDto>.Failure(
                    $"Avatar dimensions exceed the supported limit of {MaxDimension}x{MaxDimension} pixels.", 400);
            }

            using var decoded = SKBitmap.Decode(encodedData);
            if (decoded is null)
            {
                return Result<ProcessedAvatarFileDto>.Failure("The uploaded avatar is not a valid image.", 400);
            }

            using var oriented = SkiaImageOrientation.Apply(decoded, codec.EncodedOrigin);
            var cropSize = Math.Min(oriented.Width, oriented.Height);
            var cropLeft = (oriented.Width - cropSize) / 2f;
            var cropTop = (oriented.Height - cropSize) / 2f;

            using var normalized = new SKBitmap(
                AvatarSize,
                AvatarSize,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using (var canvas = new SKCanvas(normalized))
            using (var paint = new SKPaint { IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(
                    oriented,
                    new SKRect(cropLeft, cropTop, cropLeft + cropSize, cropTop + cropSize),
                    new SKRect(0, 0, AvatarSize, AvatarSize),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                    paint);
                canvas.Flush();
            }

            using var image = SKImage.FromBitmap(normalized);
            using var webp = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            if (webp is null)
            {
                return Result<ProcessedAvatarFileDto>.Failure("The uploaded avatar could not be processed.", 400);
            }

            var output = new MemoryStream(webp.ToArray(), writable: false);
            var outputName = Path.GetFileNameWithoutExtension(originalFileName) + ".webp";
            return Result<ProcessedAvatarFileDto>.Success(
                new ProcessedAvatarFileDto(output, outputName, "image/webp"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<ProcessedAvatarFileDto>.Failure("The uploaded avatar is not a valid image.", 400);
        }
    }
}
