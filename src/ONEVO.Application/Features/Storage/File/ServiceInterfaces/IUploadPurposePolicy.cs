using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

public interface IUploadPurposePolicy
{
    /// <summary>
    /// Validates size, content type, and extension against the purpose's rule.
    /// Must be called before any quota reservation or storage write.
    /// </summary>
    Result ValidateUpload(string purpose, string originalFileName, string contentType, long fileSizeBytes);

    /// <summary>
    /// SEC-13 hardening (2026-10-12): inspects the first bytes of <paramref name="content"/>
    /// against a magic-number signature table for <paramref name="originalFileName"/>'s extension,
    /// instead of trusting the caller-supplied Content-Type/extension alone. Task 23: every
    /// allow-listed extension has a check (including chat video/audio and .csv), and an extension
    /// with no check is rejected (fail closed). Does not consume/advance
    /// <paramref name="content"/>'s position for the caller (it is reset before returning).
    /// </summary>
    Result ValidateSignature(string originalFileName, Stream content);

    /// <summary>Strips the client-provided file name down to a safe charset.</summary>
    string SanitizeFileName(string originalFileName);

    /// <summary>
    /// Generates a fully server-controlled, tenant-partitioned object key.
    /// Never derived from a client-provided path or name: the object name is server-generated and
    /// keeps only the sanitized extension of <paramref name="safeFileName"/>.
    /// </summary>
    string GenerateStorageKey(Guid tenantId, Guid reservationId, string purpose, string safeFileName);
}
