using SkiaSharp;
using ONEVO.Infrastructure.Services.Storage.File;

namespace ONEVO.Tests.Unit.Features.Storage.File;

/// <summary>Task 23: uploaded images are re-encoded so metadata and hidden bytes never reach storage.</summary>
public sealed class SkiaImageSanitizerTests
{
    private readonly SkiaImageSanitizer _sanitizer = new();

    [Fact]
    public async Task SanitizeAsync_JpegWithGpsExif_RemovesTheExifAndKeepsAnUprightJpeg()
    {
        // Orientation 6 = "rotate 90° clockwise to display", as phones write for portrait photos.
        var input = TestImages.JpegWithGpsExif(width: 40, height: 20, orientation: 6);
        using (var inputData = SKData.CreateCopy(input))
        using (var inputCodec = SKCodec.Create(inputData))
        {
            // The test image really does carry the EXIF block the sanitizer must remove.
            Assert.NotNull(inputCodec);
            Assert.Equal(SKEncodedOrigin.RightTop, inputCodec!.EncodedOrigin);
            Assert.True(TestImages.Contains(input, TestImages.ExifHeader));
        }

        var result = await _sanitizer.SanitizeAsync("check-in.jpg", new MemoryStream(input));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/jpeg", result.Value!.ContentType);
        var output = ((MemoryStream)result.Value.Content).ToArray();
        Assert.False(TestImages.Contains(output, TestImages.ExifHeader), "EXIF (with GPS) must not survive re-encoding.");
        Assert.False(TestImages.Contains(output, new byte[] { 0xFF, 0xE1 }), "No APP1 (EXIF/XMP) segment may remain.");

        using var outputData = SKData.CreateCopy(output);
        using var codec = SKCodec.Create(outputData);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        // The rotation was baked into the pixels instead of being lost with the EXIF.
        Assert.Equal(20, codec.Info.Width);
        Assert.Equal(40, codec.Info.Height);
    }

    [Fact]
    public async Task SanitizeAsync_PngWithBytesHiddenAfterTheImage_DropsThem()
    {
        var hidden = "<script>alert(document.domain)</script>"u8.ToArray();
        var input = TestImages.Encode(SKEncodedImageFormat.Png, 32, 32).Concat(hidden).ToArray();

        var result = await _sanitizer.SanitizeAsync("logo.png", new MemoryStream(input));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/png", result.Value!.ContentType);
        var output = ((MemoryStream)result.Value.Content).ToArray();
        Assert.False(TestImages.Contains(output, hidden));
        using var codec = SKCodec.Create(SKData.CreateCopy(output));
        Assert.Equal(SKEncodedImageFormat.Png, codec!.EncodedFormat);
        Assert.Equal(32, codec.Info.Width);
    }

    [Fact]
    public async Task SanitizeAsync_Webp_StaysWebp()
    {
        var input = TestImages.Encode(SKEncodedImageFormat.Webp, 24, 16);

        var result = await _sanitizer.SanitizeAsync("banner.webp", new MemoryStream(input));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/webp", result.Value!.ContentType);
        using var codec = SKCodec.Create(SKData.CreateCopy(((MemoryStream)result.Value.Content).ToArray()));
        Assert.Equal(SKEncodedImageFormat.Webp, codec!.EncodedFormat);
    }

    [Fact]
    public async Task SanitizeAsync_ExeNamedPng_IsRejected()
    {
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };

        var result = await _sanitizer.SanitizeAsync("photo.png", new MemoryStream(exe));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SanitizeAsync_JpegBytesNamedPng_IsRejected()
    {
        // The decoded format must match the name, so a stored ".png" is always a PNG.
        var jpeg = TestImages.Encode(SKEncodedImageFormat.Jpeg, 8, 8);

        var result = await _sanitizer.SanitizeAsync("photo.png", new MemoryStream(jpeg));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SanitizeAsync_DimensionsOverTheLimit_AreRejectedBeforeDecoding()
    {
        using var bitmap = new SKBitmap(SkiaImageSanitizer.MaxDimension + 1, 1);
        using var image = SKImage.FromBitmap(bitmap);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        var result = await _sanitizer.SanitizeAsync("wide.png", new MemoryStream(png));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.pdf", false)]
    public void CanSanitize_OnlyRasterFormatsItCanReencode(string fileName, bool expected)
    {
        Assert.Equal(expected, _sanitizer.CanSanitize(fileName));
    }
}
