using SkiaSharp;

namespace ONEVO.Infrastructure.Services.Storage.File;

/// <summary>
/// Bakes an EXIF orientation into the pixels. Re-encoding drops EXIF, so without this a phone photo
/// taken sideways would be stored sideways. Shared by AvatarImageProcessor and SkiaImageSanitizer.
/// </summary>
internal static class SkiaImageOrientation
{
    public static SKBitmap Apply(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsDimensions = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;
        var target = new SKBitmap(
            swapsDimensions ? source.Height : source.Width,
            swapsDimensions ? source.Width : source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, source.Width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, source.Width, 0, -1, source.Height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, source.Height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, source.Height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, source.Height, -1, 0, source.Width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, source.Width),
            _ => SKMatrix.CreateIdentity()
        };

        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(
            source,
            0,
            0,
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        canvas.Flush();
        return target;
    }

    private static SKMatrix Matrix(
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY)
    {
        return new SKMatrix
        {
            ScaleX = scaleX,
            SkewX = skewX,
            TransX = transX,
            SkewY = skewY,
            ScaleY = scaleY,
            TransY = transY,
            Persp2 = 1
        };
    }
}
