namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Platform-level storage quota fallback configuration.
/// </summary>
public sealed class StorageQuotaOptions
{
    public const string SectionName = "StorageQuota";

    /// <summary>
    /// Fallback allowance in gigabytes used only when the tenant's active
    /// subscription contributes no storage. Null (the default) means there is
    /// no platform fallback and such tenants are denied storage (fail safe).
    /// Zero or negative values are treated the same as null.
    /// </summary>
    public long? DefaultStorageLimitGb { get; set; }
}
