using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.SignalR;
using ONEVO.Infrastructure.Observability;
using ONEVO.Infrastructure.SharedState;
using StackExchange.Redis;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Task 21, the API's part of shared state (the rest is in Infrastructure's <see cref="SharedStateRegistration"/>):
/// <list type="bullet">
/// <item>Data protection always uses one application name, so instances can read each other's protected data. With
/// Redis the key ring lives there too: the sign-in cookie is protected with it, so without a shared ring a user signed
/// in through instance A would be signed out on instance B.</item>
/// <item>With Redis: the SignalR backplane, the shared presence store and its refresher, and a Degraded readiness
/// check.</item>
/// </list>
/// </summary>
public static class SharedStateExtensions
{
    public const string DataProtectionApplicationName = "crownus-api";
    public static readonly RedisKey DataProtectionKey = SharedStateRegistration.KeyPrefix + "data-protection-keys";
    public static readonly RedisChannel SignalRChannelPrefix = RedisChannel.Literal(SharedStateRegistration.KeyPrefix + "signalr");

    public static void AddApiSharedState(
        this IServiceCollection services, ISignalRServerBuilder signalR, IHealthChecksBuilder health, IConfiguration configuration)
    {
        services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);

        if (!SharedStateRegistration.IsRedisEnabled(configuration))
            return;

        var connectionString = configuration[SharedStateRegistration.ConnectionStringKey]!;
        services.AddOptions<KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((options, redis) =>
                options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), DataProtectionKey));

        signalR.AddStackExchangeRedis(options =>
        {
            options.Configuration = SharedStateRegistration.RedisOptions(connectionString);
            options.Configuration.ChannelPrefix = SignalRChannelPrefix;
        });

        services.AddSingleton<ISharedPresenceStore, RedisPresenceStore>();
        services.AddHostedService<ONEVO.Api.Hubs.TrayPresenceRefresher>();
        health.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
    }
}
