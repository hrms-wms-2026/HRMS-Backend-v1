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
        var dataProtection = services.AddDataProtection();
        services.AddOptions<KeyManagementOptions>()
            .Configure<IConnectionMultiplexer>((options, redis) =>
                options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), DataProtectionKey));
        // The key ring protects sign-in cookies. In Redis it is readable by anyone with the Redis access key, so
        // encrypt it at rest with a certificate when one is configured (docs/deployment/shared-state.md).
        if (LoadKeyEncryptionCertificate(configuration) is { } certificate)
            dataProtection.ProtectKeysWithCertificate(certificate);

        signalR.AddStackExchangeRedis(options =>
        {
            options.Configuration = SharedStateRegistration.RedisOptions(connectionString);
            options.Configuration.ChannelPrefix = SignalRChannelPrefix;
        });

        services.AddSingleton<ISharedPresenceStore, RedisPresenceStore>();
        services.AddHostedService<ONEVO.Api.Hubs.TrayPresenceRefresher>();
        health.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
    }

    public const string KeyEncryptionCertificateKey = "DataProtection:KeyEncryptionCertificate";
    public const string KeyEncryptionCertificatePasswordKey = "DataProtection:KeyEncryptionCertificatePassword";

    /// <summary>A base64 PFX (secret) and its password, from the host secret store. Null when not configured.</summary>
    public static System.Security.Cryptography.X509Certificates.X509Certificate2? LoadKeyEncryptionCertificate(IConfiguration configuration)
    {
        var pfx = configuration[KeyEncryptionCertificateKey];
        if (string.IsNullOrWhiteSpace(pfx))
            return null;
        return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(pfx), configuration[KeyEncryptionCertificatePasswordKey]);
    }

    /// <summary>Call once the app is built: says so in the log when the shared key ring is stored unencrypted.</summary>
    public static void WarnIfKeyRingUnencrypted(WebApplication app)
    {
        if (SharedStateRegistration.IsRedisEnabled(app.Configuration)
            && string.IsNullOrWhiteSpace(app.Configuration[KeyEncryptionCertificateKey]))
        {
            app.Logger.LogWarning(
                "The data-protection key ring is stored in Redis without encryption. Set {Setting} to encrypt it at rest.",
                KeyEncryptionCertificateKey);
        }
    }
}
