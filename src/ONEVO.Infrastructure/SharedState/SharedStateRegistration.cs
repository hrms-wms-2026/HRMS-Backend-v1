using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace ONEVO.Infrastructure.SharedState;

/// <summary>
/// Task 21: state that several API instances must share. With <c>Redis:ConnectionString</c> set, the pieces below use
/// Redis; without it, each keeps its in-process version, so Development and a single instance behave as before.
/// Nothing Redis-related is registered or resolved when the setting is absent.
/// <list type="bullet">
/// <item>cache invalidation (session tickets, tenants, <c>ICacheService</c>) through one pub/sub bus;</item>
/// <item>permission versions, admin MFA challenges, sign-in attempt counters;</item>
/// <item>web and tray presence (registered by the API project, which owns the hubs).</item>
/// </list>
/// Docs: docs/deployment/shared-state.md.
/// </summary>
public static class SharedStateRegistration
{
    public const string ConnectionStringKey = "Redis:ConnectionString";

    /// <summary>Prefix of every Redis key and channel the API uses, so one Redis can be shared safely.</summary>
    public const string KeyPrefix = "crownus:";

    public static bool IsRedisEnabled(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]);

    /// <summary>A configured but unreachable Redis does not stop start-up (AbortOnConnectFail=false): the
    /// multiplexer keeps reconnecting, the Redis readiness check reports Degraded, and each piece falls back as
    /// its class describes.</summary>
    public static ConfigurationOptions RedisOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ClientName ??= "crownus-api";
        return options;
    }

    public static IServiceCollection AddSharedState(this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsRedisEnabled(configuration))
        {
            services.TryAddSingleton<ISharedInvalidationBus, LocalInvalidationBus>();
            services.TryAddSingleton<IAttemptCounter, MemoryAttemptCounter>();
            return services;
        }

        var connectionString = configuration[ConnectionStringKey]!;
        services.TryAddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(RedisOptions(connectionString)));
        services.TryAddSingleton<RedisInvalidationBus>();
        services.TryAddSingleton<ISharedInvalidationBus>(sp => sp.GetRequiredService<RedisInvalidationBus>());
        services.AddHostedService(sp => sp.GetRequiredService<RedisInvalidationBus>());
        services.TryAddSingleton<MemoryAttemptCounter>();
        services.TryAddSingleton<IAttemptCounter, RedisAttemptCounter>();
        return services;
    }
}
