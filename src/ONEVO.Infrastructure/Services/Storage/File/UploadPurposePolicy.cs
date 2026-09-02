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
