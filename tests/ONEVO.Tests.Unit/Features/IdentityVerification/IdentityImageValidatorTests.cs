using ONEVO.Application.Features.IdentityVerification.Services;

namespace ONEVO.Tests.Unit.Features.IdentityVerification;

public sealed class IdentityImageValidatorTests
{
    private readonly IdentityImageValidator _validator = new();

    [Fact]
    public async Task Validate_JpegMimeWithPngBytes_RejectsSpoofedContent()
    {
        await using var stream = new MemoryStream(
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ]);

        var result = await _validator.ValidateAsync(
            stream,
            "face.jpg",
            "image/jpeg",
            5 * 1024 * 1024,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Validate_ValidJpegSignature_ReturnsBufferedBytes()
    {
        await using var stream = new MemoryStream(
        [
            0xFF, 0xD8, 0xFF, 0xE0,
            0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01
        ]);

        var result = await _validator.ValidateAsync(
            stream,
            "face.jpeg",
            "image/jpeg",
            5 * 1024 * 1024,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value!.Length);
    }

    [Fact]
    public async Task Validate_OverConfiguredLimit_RejectsBeforeProviderCall()
    {
        await using var stream = new MemoryStream(new byte[1025]);
        stream.GetBuffer()[0] = 0xFF;
        stream.GetBuffer()[1] = 0xD8;
        stream.GetBuffer()[2] = 0xFF;

        var result = await _validator.ValidateAsync(
            stream,
            "face.jpg",
            "image/jpeg",
            1024,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
