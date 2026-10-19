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
    /// instead of trusting the caller-supplied Content-Type/extension alone. An extension with no
    /// entry in the table (ambiguous/unlisted format) is not rejected here - ValidateUpload's
    /// existing extension+content-type check already covers it. Does not consume/advance
    /// <paramref name="content"/>'s position for the caller (it is reset before returning).
    /// </summary>
    Result ValidateSignature(string originalFileName, Stream content);

    /// <summary>Strips the client-provided file name down to a safe charset.</summary>
    string SanitizeFileName(string originalFileName);

    /// <summary>
    /// Generates a fully server-controlled, tenant-partitioned object key.
    /// Never derived from a client-provided path — only from
    /// <paramref name="safeFileName"/> (already sanitized) plus server state.
    /// </summary>
    string GenerateStorageKey(Guid tenantId, Guid reservationId, string purpose, string safeFileName);
}
