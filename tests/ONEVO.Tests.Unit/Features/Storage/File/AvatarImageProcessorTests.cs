using SkiaSharp;
using ONEVO.Infrastructure.Services.Storage.File;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public sealed class AvatarImageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidImage_ReturnsSquareWebp()
    {
        using var sourceBitmap = new SKBitmap(600, 300);
        using (var canvas = new SKCanvas(sourceBitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }
        using var sourceImage = SKImage.FromBitmap(sourceBitmap);
        using var encoded = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        using var source = new MemoryStream(encoded.ToArray());
        var processor = new AvatarImageProcessor();

        var result = await processor.ProcessAsync("profile.png", source, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("profile.webp", result.Value!.StorageFileName);
        Assert.Equal("image/webp", result.Value.ContentType);
        await using var output = result.Value.Content;
        using var outputData = SKData.CreateCopy(((MemoryStream)output).ToArray());
        using var codec = SKCodec.Create(outputData);
        Assert.NotNull(codec);
        Assert.Equal(AvatarImageProcessor.AvatarSize, codec!.Info.Width);
        Assert.Equal(AvatarImageProcessor.AvatarSize, codec.Info.Height);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
    }

    [Fact]
    public async Task ProcessAsync_CorruptInput_ReturnsBadRequest()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var processor = new AvatarImageProcessor();

        var result = await processor.ProcessAsync("profile.png", source, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task ProcessAsync_ExcessiveSourceDimensions_ReturnsBadRequestBeforeDecode()
    {
        using var sourceBitmap = new SKBitmap(AvatarImageProcessor.MaxDimension + 1, 1);
        using var sourceImage = SKImage.FromBitmap(sourceBitmap);
        using var encoded = sourceImage.Encode(SKEncodedImageFormat.Png, 100);
        using var source = new MemoryStream(encoded.ToArray());
        var processor = new AvatarImageProcessor();

        var result = await processor.ProcessAsync("oversized.png", source, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
