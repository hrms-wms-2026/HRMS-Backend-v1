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

    public string GenerateStorageKey(Guid tenantId, string purpose, string safeFileName)
    {
        var now = DateTimeOffset.UtcNow;
        var uniqueSegment = Guid.NewGuid().ToString("N");
        return $"tenants/{tenantId:N}/{purpose}/{now:yyyy}/{now:MM}/{uniqueSegment}-{safeFileName}";
    }
}
