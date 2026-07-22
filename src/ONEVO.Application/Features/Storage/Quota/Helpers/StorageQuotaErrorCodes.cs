namespace ONEVO.Application.Features.Storage.Quota.Helpers;

/// <summary>
/// Structured error codes for storage quota failures, per the architecture
/// document rule that quota-exceeded responses use a structured error such as
/// <c>storage_quota_exceeded</c>.
/// </summary>
public static class StorageQuotaErrorCodes
{
    /// <summary>The requested bytes would push the tenant past its allowance. Maps to 409.</summary>
    public const string QuotaExceeded = "storage_quota_exceeded";

    /// <summary>No storage allowance could be resolved for the tenant. Maps to 403.</summary>
    public const string NotEntitled = "storage_not_entitled";

    /// <summary>The requested byte count is zero or negative. Maps to 400.</summary>
    public const string InvalidFileSize = "storage_invalid_file_size";

    /// <summary>The tenant id was missing/empty; quota checks fail safely. Maps to 400.</summary>
    public const string TenantContextMissing = "storage_tenant_context_missing";
}
