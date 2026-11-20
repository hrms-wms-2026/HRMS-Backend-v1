using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.Observability;

/// <summary>
/// Task 21: readiness entry for Redis, registered only when Redis is configured. Degraded (still HTTP 200), never
/// Unhealthy: every instance shares the same Redis, and each shared-state piece has a fallback, so failing
/// readiness would take the whole site out of the load balancer for a problem it can ride out.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
            return HealthCheckResult.Degraded("Redis is not connected; shared state falls back to each instance.");
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis answered in {latency.TotalMilliseconds:0} ms.");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis did not answer a ping.", ex);
        }
    }
}
