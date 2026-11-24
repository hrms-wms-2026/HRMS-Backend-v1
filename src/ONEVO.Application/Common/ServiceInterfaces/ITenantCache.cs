namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Short-lived cache for reference data that many requests read and few change (improvements task 22).
/// Keys come from <see cref="Caching.ReferenceDataCacheKeys"/> and always contain the tenant id, so one tenant can never
/// read another tenant's entry. Entries are dropped when the data is saved (reference-data save interceptor) and after
/// a few minutes at the latest. Never cache data that depends on the caller's permissions here.
/// </summary>
public interface ITenantCache
{
    /// <summary>
    /// Returns the cached value for this tenant, or runs <paramref name="load"/> and caches its result. The cache is
    /// skipped (load runs, nothing is stored) when caching is switched off or when this request's database context could
    /// not read <paramref name="tenantId"/>'s rows, so an empty result is never cached for the wrong tenant.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        Guid tenantId, string name, string variant, Func<CancellationToken, Task<T>> load, CancellationToken ct = default);

    /// <summary>Platform-wide data with no tenant column (the permission and module catalogues).</summary>
    Task<T> GetOrCreateGlobalAsync<T>(string name, Func<CancellationToken, Task<T>> load, CancellationToken ct = default);

    /// <summary>Drops every variant of <paramref name="name"/> for one tenant, on every instance.</summary>
    Task InvalidateAsync(Guid tenantId, string name, CancellationToken ct = default);

    /// <summary>Drops <paramref name="name"/> for every tenant and the global entry, on every instance.</summary>
    Task InvalidateEverywhereAsync(string name, CancellationToken ct = default);
}
