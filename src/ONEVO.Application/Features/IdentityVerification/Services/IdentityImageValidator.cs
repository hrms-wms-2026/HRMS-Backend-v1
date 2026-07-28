using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.IdentityVerification.Services;

public sealed class IdentityImageValidator : IIdentityImageValidator
{
    private static readonly HashSet<string> JpegExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg"
        };

    public async Task<Result<byte[]>> ValidateAsync(
        Stream content,
        string fileName,
        string contentType,
        int maximumBytes,
        CancellationToken ct)
    {
        if (!content.CanSeek)
        {
            return Result<byte[]>.Failure(
                "Identity image stream must be buffered.",
                400);
        }
        if (content.Length is <= 0 ||
            content.Length > maximumBytes)
        {
            return Result<byte[]>.Failure(
                "Identity image size is outside the allowed limit.",
                400);
        }

        var extension = Path.GetExtension(fileName);
        var normalizedContentType =
            contentType.Trim().ToLowerInvariant();
        var typeAndExtensionMatch =
            normalizedContentType == "image/png" &&
            string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
            normalizedContentType == "image/jpeg" &&
            JpegExtensions.Contains(extension);
        if (!typeAndExtensionMatch)
        {
            return Result<byte[]>.Failure(
                "Identity image must be a PNG or JPEG with a matching extension.",
                400);
        }

        var bytes = new byte[content.Length];
        content.Position = 0;
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = await content.ReadAsync(
                bytes.AsMemory(totalRead),
                ct);
            if (read == 0)
                break;
            totalRead += read;
        }
        content.Position = 0;
        if (totalRead != bytes.Length ||
            !SignatureMatches(bytes, normalizedContentType))
        {
            return Result<byte[]>.Failure(
                "Identity image content does not match its declared type.",
                400);
        }

        return Result<byte[]>.Success(bytes);
    }

    private static bool SignatureMatches(
        byte[] bytes,
        string contentType)
    {
        if (contentType == "image/jpeg")
        {
            return bytes.Length >= 3 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF;
        }

        ReadOnlySpan<byte> pngSignature =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];
        return bytes.Length >= pngSignature.Length &&
            bytes.AsSpan(0, pngSignature.Length)
                .SequenceEqual(pngSignature);
    }
}

