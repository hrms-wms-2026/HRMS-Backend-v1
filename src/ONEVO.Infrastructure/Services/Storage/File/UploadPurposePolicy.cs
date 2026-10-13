using System.Text;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Storage.File;

public sealed class UploadPurposePolicy : IUploadPurposePolicy
{
    private static readonly char[] SafeCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    public Result ValidateUpload(string purpose, string originalFileName, string contentType, long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(purpose) || !UploadPurposeCatalog.IsSupported(purpose))
        {
            return Result.Failure(
                $"purpose must be one of: {string.Join(", ", UploadPurposeCatalog.SupportedPurposes)}.", 400);
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return Result.Failure("originalFileName is required.", 400);
        }

        if (fileSizeBytes <= 0)
        {
            return Result.Failure("fileSizeBytes must be greater than zero.", 400);
        }

        var rule = UploadPurposeCatalog.GetRule(purpose)!;

        if (fileSizeBytes > rule.MaxSizeBytes)
        {
            return Result.Failure(
                $"File exceeds the maximum allowed size of {rule.MaxSizeBytes} bytes for purpose '{purpose}'.", 400);
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !rule.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure(
                $"contentType '{contentType}' is not allowed for purpose '{purpose}'.", 400);
        }

        var extension = System.IO.Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension)
            || !rule.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure(
                $"File extension '{extension}' is not allowed for purpose '{purpose}'.", 400);
        }

        if (!ContentTypeMatchesExtension(contentType, extension))
        {
            return Result.Failure(
                $"contentType '{contentType}' does not match file extension '{extension}'.", 400);
        }

        return Result.Success();
    }

    // Magic-number signatures for the extensions ValidateUpload's rules actually allow today
    // (UploadPurposeCatalog.cs). .docx/.xlsx share the ZIP signature at the byte level - that is
    // intentional, not a bug: Office Open XML documents ARE zip archives. Extensions with no entry
    // here (anything not covered by an UploadPurposeRule's AllowedExtensions) are left to
    // ValidateUpload's existing extension+content-type check; this table never grows the allowlist,
    // only adds a second, byte-level check on top of it.
    private static readonly IReadOnlyDictionary<string, byte[][]> Signatures = new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"] = [[0x47, 0x49, 0x46, 0x38]],
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],
        [".zip"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".doc"] = [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]],
        [".xls"] = [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]],
        // RIFF....WEBP - the "WEBP" tag sits at offset 8, checked separately below.
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    public Result ValidateSignature(string originalFileName, Stream content)
    {
        var extension = System.IO.Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!Signatures.TryGetValue(extension, out var candidates))
        {
            // Ambiguous/unlisted extension - nothing to check against, handled explicitly by
            // doing nothing rather than guessing or rejecting.
            return Result.Success();
        }

        if (!content.CanSeek)
        {
            // Can't rewind to re-read after peeking - fail closed rather than skip the check.
            return Result.Failure("File content could not be inspected for signature verification.", 400);
        }

        var header = new byte[16];
        var originalPosition = content.Position;
        content.Position = 0;
        var read = content.Read(header, 0, header.Length);
        content.Position = originalPosition;

        var matches = candidates.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig));
        if (matches && extension == ".webp")
        {
            // RIFF container alone isn't enough - confirm the WEBP tag at offset 8.
            matches = read >= 12 && header.AsSpan(8, 4).SequenceEqual(new byte[] { 0x57, 0x45, 0x42, 0x50 });
        }

        return matches
            ? Result.Success()
            : Result.Failure($"File content does not match the expected format for extension '{extension}'.", 400);
    }

    /// <summary>Best-effort content-type sniffed from the same signature table used by
    /// <see cref="ValidateSignature"/> - null when the extension isn't in the table (ambiguous) or
    /// the bytes don't match anything recognized. Used only to populate FileRecord.DetectedContentType
    /// for audit/telemetry; never used to grant access or override the declared Content-Type.</summary>
    public static string? DetectContentType(string originalFileName, Stream content)
    {
        var extension = System.IO.Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!Signatures.ContainsKey(extension) || !content.CanSeek)
            return null;

        var header = new byte[16];
        var originalPosition = content.Position;
        content.Position = 0;
        var read = content.Read(header, 0, header.Length);
        content.Position = originalPosition;
        if (read <= 0)
            return null;

        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".webp" when read >= 12 && header.AsSpan(8, 4).SequenceEqual(new byte[] { 0x57, 0x45, 0x42, 0x50 }) => "image/webp",
            ".zip" => "application/zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".doc" => "application/msword",
            ".xls" => "application/vnd.ms-excel",
            _ => null
        };
    }

    public string SanitizeFileName(string originalFileName)
    {
        var nameOnly = System.IO.Path.GetFileName(originalFileName);
        var extension = System.IO.Path.GetExtension(nameOnly);
        var baseName = System.IO.Path.GetFileNameWithoutExtension(nameOnly);

        var builder = new StringBuilder();
        foreach (var character in baseName)
        {
            if (Array.IndexOf(SafeCharacters, character) >= 0)
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('-');
            }
        }

        var safeBaseName = builder.ToString().Trim('-', '.');
        if (safeBaseName.Length == 0)
        {
            safeBaseName = "file";
        }

        if (safeBaseName.Length > 100)
        {
            safeBaseName = safeBaseName.Substring(0, 100);
        }

        var safeExtensionBuilder = new StringBuilder();
        foreach (var character in extension)
        {
            if (Array.IndexOf(SafeCharacters, character) >= 0)
            {
                safeExtensionBuilder.Append(character);
            }
        }

        return safeBaseName + safeExtensionBuilder.ToString().ToLowerInvariant();
    }

    public string GenerateStorageKey(Guid tenantId, Guid reservationId, string purpose, string safeFileName)
    {
        return $"tenants/{tenantId:N}/{purpose}/{reservationId:N}/{safeFileName}";
    }

    private static bool ContentTypeMatchesExtension(string contentType, string extension)
    {
        return extension switch
        {
            ".png" => contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".webp" => contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase),
            ".gif" => contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase),
            ".pdf" => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".doc" => contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase),
            ".docx" => contentType.Equals(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                StringComparison.OrdinalIgnoreCase),
            // .xls/.xlsx/.zip each accept a second, commonly-reported fallback content
            // type alongside their canonical one — browsers are inconsistent about
            // what they send for these three extensions specifically. Every other
            // extension keeps a single exact match, unchanged.
            ".xls" => contentType.Equals("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase),
            ".xlsx" => contentType.Equals(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase),
            ".zip" => contentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
