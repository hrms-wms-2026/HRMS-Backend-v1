using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeAvatarImageProcessor : IAvatarImageProcessor
{
    public int CallCount { get; private set; }

    public async Task<Result<ProcessedAvatarFileDto>> ProcessAsync(
        string originalFileName,
        Stream content,
        CancellationToken ct = default)
    {
        CallCount++;
        var output = new MemoryStream();
        if (content.CanSeek)
            content.Position = 0;

        await content.CopyToAsync(output, ct);
        output.Position = 0;
        return Result<ProcessedAvatarFileDto>.Success(new ProcessedAvatarFileDto(
            output,
            Path.GetFileNameWithoutExtension(originalFileName) + ".webp",
            "image/webp"));
    }
}
