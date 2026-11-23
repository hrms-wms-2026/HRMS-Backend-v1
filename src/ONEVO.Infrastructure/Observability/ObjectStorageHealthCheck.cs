using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Infrastructure.Observability;

/// <summary>
/// Task 17: readiness entry for object storage. Asks the storage provider whether a fixed key exists - a read-only
/// call that proves the provider keys, the network path and the bucket work, whatever the answer. Degraded (still
/// HTTP 200), never Unhealthy: a storage outage hits every instance alike, and only file features need it.
/// </summary>
public sealed class ObjectStorageHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public const string ProbeKey = "health-check/readiness-probe";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            // Provider keys live in platform_service_keys, which System mode reads.
            scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorageAdapter>();
            await storage.ObjectExistsAsync(ProbeKey, limit.Token);
            return HealthCheckResult.Healthy("Object storage answered.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"Object storage did not answer within {Timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Object storage call failed.", ex);
        }
    }
}
