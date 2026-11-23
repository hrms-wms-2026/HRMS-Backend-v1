using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

/// <summary>
/// Stands in for the SkiaSharp avatar processor: reads the input and returns a fixed minimal WebP
/// header (RIFF....WEBP), like the real processor always returns WebP whatever it was given.
/// </summary>
public sealed class FakeAvatarImageProcessor : IAvatarImageProcessor
{
    public static readonly byte[] WebpBytes = { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };

    public int CallCount { get; private set; }

    public async Task<Result<ProcessedAvatarFileDto>> ProcessAsync(
        string originalFileName,
        Stream content,
        CancellationToken ct = default)
    {
        CallCount++;
        if (content.CanSeek)
            content.Position = 0;
        await content.CopyToAsync(Stream.Null, ct);

        return Result<ProcessedAvatarFileDto>.Success(new ProcessedAvatarFileDto(
            new MemoryStream(WebpBytes, writable: false),
            Path.GetFileNameWithoutExtension(originalFileName) + ".webp",
            "image/webp"));
    }
}
