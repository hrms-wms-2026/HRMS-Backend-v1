using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

public interface IUploadPurposePolicy
{
    /// <summary>
    /// Validates size, content type, and extension against the purpose's rule.
    /// Must be called before any quota reservation or storage write.
    /// </summary>
    Result ValidateUpload(string purpose, string originalFileName, string contentType, long fileSizeBytes);

    /// <summary>Strips the client-provided file name down to a safe charset.</summary>
    string SanitizeFileName(string originalFileName);

    /// <summary>
    /// Generates a fully server-controlled, tenant-partitioned object key.
    /// Never derived from a client-provided path — only from
    /// <paramref name="safeFileName"/> (already sanitized) plus server state.
    /// </summary>
    string GenerateStorageKey(Guid tenantId, string purpose, string safeFileName);
}
