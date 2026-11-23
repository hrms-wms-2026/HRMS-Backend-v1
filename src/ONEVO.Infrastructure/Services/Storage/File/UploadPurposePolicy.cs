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

    // Magic-number signatures for the extensions ValidateUpload's rules allow (UploadPurposeCatalog.cs).
    // .docx/.xlsx share the ZIP signature at the byte level - that is intentional, not a bug: Office
    // Open XML documents ARE zip archives. Formats whose signature is not a fixed prefix (WebP, WAV,
    // MP4/MOV/M4A, MP3, CSV) are checked in MatchesSignature below. This table never grows the
    // allow-list; it only adds a byte-level check on top of it.
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

    // Task 23: the chat video/audio formats and bulk-onboarding CSV used to pass unchecked, because
    // they had no entry above. Every allowed extension now has a check, and an extension with none
    // is refused (fail closed) instead of being let through.
    private static readonly HashSet<string> FtypContainers = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4a" };

    private const int CsvInspectBytes = 8 * 1024;

    public Result ValidateSignature(string originalFileName, Stream content)
    {
        var extension = System.IO.Path.GetExtension(originalFileName).ToLowerInvariant();

        if (!content.CanSeek)
        {
            // Can't rewind to re-read after peeking - fail closed rather than skip the check.
            return Result.Failure("File content could not be inspected for signature verification.", 400);
        }

        var header = new byte[extension == ".csv" ? CsvInspectBytes : 16];
        var originalPosition = content.Position;
        content.Position = 0;
        var read = ReadUpTo(content, header);
        content.Position = originalPosition;

        var known = MatchesSignature(extension, header.AsSpan(0, read));
        return known switch
        {
            true => Result.Success(),
            false => Result.Failure($"File content does not match the expected format for extension '{extension}'.", 400),
            null => Result.Failure($"Files with extension '{extension}' cannot be verified and are not accepted.", 400),
        };
    }

    /// <summary>True/false when the extension has a check; null when it has none (refused).</summary>
    private static bool? MatchesSignature(string extension, ReadOnlySpan<byte> header)
    {
        if (Signatures.TryGetValue(extension, out var candidates))
        {
            var prefixMatches = false;
            foreach (var sig in candidates)
            {
                if (header.Length >= sig.Length && header[..sig.Length].SequenceEqual(sig))
                    prefixMatches = true;
            }

            // RIFF container alone isn't enough - confirm the WEBP tag at offset 8.
            if (prefixMatches && extension == ".webp")
                return header.Length >= 12 && header.Slice(8, 4).SequenceEqual("WEBP"u8);

            return prefixMatches;
        }

        if (FtypContainers.Contains(extension))
            return header.Length >= 8 && header.Slice(4, 4).SequenceEqual("ftyp"u8);

        return extension switch
        {
            ".webm" => header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            ".ogg" => header.Length >= 4 && header[..4].SequenceEqual("OggS"u8),
            ".wav" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8),
            // ID3 tag, or a bare MPEG audio frame (11 sync bits set).
            ".mp3" => header.Length >= 3 && (header[..3].SequenceEqual("ID3"u8)
                || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
            ".csv" => LooksLikeText(header),
            _ => null,
        };
    }

    /// <summary>CSV has no signature: accept text, refuse anything with NUL bytes or a known binary header.</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        if (header.Length == 0)
            return false;
        if (header.IndexOf((byte)0) >= 0)
            return false;
        if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
            return false; // Windows executable
        if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x7F, 0x45, 0x4C, 0x46 }))
            return false; // ELF executable
        if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }))
            return false; // ZIP/XLSX renamed to .csv
        return true;
    }

    private static int ReadUpTo(Stream content, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = content.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }

        return total;
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
        // Task 23: the object name is generated here, never taken from the client. Only the
        // (already sanitized and allow-listed) extension is kept, so the stored type stays visible.
        // The client's name is kept for display in file_records.original/safe_file_name only.
        return $"tenants/{tenantId:N}/{purpose}/{reservationId:N}/{ServerObjectName(safeFileName)}";
    }

    /// <summary>"content" plus the file's sanitized extension, e.g. "content.pdf".</summary>
    public static string ServerObjectName(string safeFileName)
    {
        var extension = System.IO.Path.GetExtension(System.IO.Path.GetFileName(safeFileName)).ToLowerInvariant();
        var safeExtension = new string(extension.Where(c => Array.IndexOf(SafeCharacters, c) >= 0).ToArray());
        return "content" + (safeExtension.Length > 1 ? safeExtension : string.Empty);
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
            // Chat video and audio (conversation_attachment). Browsers report these inconsistently, so each extension
            // accepts the types they are known to send.
            ".mp4" => contentType.Equals("video/mp4", StringComparison.OrdinalIgnoreCase),
            ".mov" => contentType.Equals("video/quicktime", StringComparison.OrdinalIgnoreCase),
            ".webm" => contentType.Equals("video/webm", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("audio/webm", StringComparison.OrdinalIgnoreCase),
            ".mp3" => contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase),
            ".wav" => contentType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("audio/wave", StringComparison.OrdinalIgnoreCase),
            ".m4a" => contentType.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("audio/x-m4a", StringComparison.OrdinalIgnoreCase),
            ".ogg" => contentType.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("video/ogg", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
