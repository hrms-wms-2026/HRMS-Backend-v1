using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.AppCatalog.DTOs;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo;

public sealed record SetGlobalApplicationLogoCommand(
    Guid Id,
    byte[] Content,
    string ContentType,
    Guid ActorId) : IRequest<Result<GlobalApplicationDto>>;

public sealed class SetGlobalApplicationLogoCommandHandler(
    IGlobalApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IImageSanitizer imageSanitizer)
    : IRequestHandler<SetGlobalApplicationLogoCommand, Result<GlobalApplicationDto>>
{
    private static readonly IReadOnlySet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif"
    };

    public async Task<Result<GlobalApplicationDto>> Handle(SetGlobalApplicationLogoCommand request, CancellationToken ct)
    {
        var app = await applications.GetByIdAsync(request.Id, ct);
        if (app is null)
            return Result<GlobalApplicationDto>.Failure("Application not found.", 404);
        if (request.Content.Length is 0 or > 2 * 1024 * 1024)
            return Result<GlobalApplicationDto>.Failure("Logo must be between 1 byte and 2 MB.", 400);
        if (!AllowedTypes.Contains(request.ContentType))
            return Result<GlobalApplicationDto>.Failure("Logo must be PNG, JPEG, WebP, or GIF.", 400);
        if (!HasExpectedSignature(request.Content, request.ContentType))
            return Result<GlobalApplicationDto>.Failure("Logo content does not match its declared image type.", 400);

        // Task 23: PNG, JPEG and WebP logos are re-encoded, which drops metadata and hidden bytes.
        // GIF cannot be re-encoded without losing animation; it is kept as sent and, like every file
        // the API returns, served as an attachment with nosniff (UserFileResponseFilter).
        var logo = request.Content;
        var logoContentType = request.ContentType.ToLowerInvariant();
        var sanitizerName = logoContentType switch
        {
            "image/png" => "logo.png",
            "image/jpeg" => "logo.jpg",
            "image/webp" => "logo.webp",
            _ => null
        };
        if (sanitizerName is not null)
        {
            using var source = new MemoryStream(request.Content, writable: false);
            var sanitized = await imageSanitizer.SanitizeAsync(sanitizerName, source, ct);
            if (!sanitized.IsSuccess)
                return Result<GlobalApplicationDto>.Failure(sanitized.Error!, sanitized.StatusCode ?? 400);
            await using var output = sanitized.Value!.Content;
            using var buffer = new MemoryStream();
            await output.CopyToAsync(buffer, ct);
            logo = buffer.ToArray();
            logoContentType = sanitized.Value.ContentType;
        }

        app.Logo = logo;
        app.LogoContentType = logoContentType;
        app.CreatedById ??= request.ActorId;
        app.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);
        return Result<GlobalApplicationDto>.Success(GlobalApplicationDto.FromEntity(app));
    }

    private static bool HasExpectedSignature(byte[] content, string contentType)
    {
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 6 && (content.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || content.AsSpan(0, 6).SequenceEqual("GIF89a"u8));
        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        return false;
    }
}
