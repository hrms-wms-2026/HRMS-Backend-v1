using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

/// <summary>
/// Pass-through stand-in for SkiaImageSanitizer, so FileStorageService tests can use tiny fake
/// images (magic bytes only). SkiaImageSanitizerTests cover the real re-encoding.
/// </summary>
public sealed class FakeImageSanitizer : IImageSanitizer
{
    public int CallCount { get; private set; }

    public bool CanSanitize(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    public async Task<Result<SanitizedImage>> SanitizeAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        CallCount++;
        var output = new MemoryStream();
        if (content.CanSeek)
            content.Position = 0;
        await content.CopyToAsync(output, ct);
        output.Position = 0;
        var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return Result<SanitizedImage>.Success(new SanitizedImage(output, contentType));
    }
}
