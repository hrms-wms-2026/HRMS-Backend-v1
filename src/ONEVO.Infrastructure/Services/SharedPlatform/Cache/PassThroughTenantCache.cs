using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

/// <summary>No caching: every read runs its query. Used where a repository is built without the DI container.</summary>
public sealed class PassThroughTenantCache : ITenantCache
{
    public static PassThroughTenantCache Instance { get; } = new();

    public Task<T> GetOrCreateAsync<T>(
        Guid tenantId, string name, string variant, Func<CancellationToken, Task<T>> load, CancellationToken ct = default) =>
        load(ct);

    public Task<T> GetOrCreateGlobalAsync<T>(string name, Func<CancellationToken, Task<T>> load, CancellationToken ct = default) =>
        load(ct);

    public Task InvalidateAsync(Guid tenantId, string name, CancellationToken ct = default) => Task.CompletedTask;

    public Task InvalidateEverywhereAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
}
