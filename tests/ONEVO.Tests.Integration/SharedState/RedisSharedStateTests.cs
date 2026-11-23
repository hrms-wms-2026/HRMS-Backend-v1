using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Api.Extensions;
using ONEVO.Api.Hubs;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.SharedState;
using StackExchange.Redis;
using Xunit;

namespace ONEVO.Tests.Integration.SharedState;

/// <summary>
/// Task 21: two simulated API instances (two Redis connections, two sets of local caches) on one Redis. Each test
/// shows that what one instance does is seen by the other.
/// </summary>
public sealed class RedisSharedStateTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Soon;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(50);
        }
    }

    // ---- invalidation bus + session ticket cache ------------------------------------------------

    private sealed record Instance(SessionTicketCache Local, BroadcastingSessionTicketCache Shared, RedisInvalidationBus Bus);

    private async Task<Instance> StartInstanceAsync()
    {
        var mux = await redis.ConnectAsync();
        var local = new SessionTicketCache(new MemoryCache(new MemoryCacheOptions()), new SystemDateTimeProvider(),
            Options.Create(new SessionTicketCacheOptions { LifetimeSeconds = 30, InvalidationGraceSeconds = 0 }));
        var services = new ServiceCollection()
            .AddSingleton<IInvalidationHandler>(new SessionTicketInvalidationHandler(local))
            .BuildServiceProvider();
        var bus = new RedisInvalidationBus(mux, services, NullLogger<RedisInvalidationBus>.Instance);
        await bus.StartAsync(CancellationToken.None);
        return new Instance(local, new BroadcastingSessionTicketCache(local, bus), bus);
    }

    private static void Cache(SessionTicketCache cache, string keyHash, Guid userId, Guid tenantId)
        => cache.Set(keyHash, new object(), userId, tenantId, DateTimeOffset.UtcNow.AddMilliseconds(-10));

    [Fact]
    public async Task SessionRevokedOnA_IsNoLongerServedFromBsCache()
    {
        var a = await StartInstanceAsync();
        var b = await StartInstanceAsync();
        var keyHash = $"session-{Guid.NewGuid():N}";
        Cache(b.Local, keyHash, Guid.NewGuid(), Guid.NewGuid());
        Assert.True(b.Local.TryGet<object>(keyHash, out _));

        a.Shared.EvictSession(keyHash);

        await Eventually(() => !b.Local.TryGet<object>(keyHash, out _), "instance B kept serving a session revoked on A");
    }

    [Fact]
    public async Task UserAndTenantEvictions_ReachTheOtherInstance()
    {
        var a = await StartInstanceAsync();
        var b = await StartInstanceAsync();
        var (user, tenant) = (Guid.NewGuid(), Guid.NewGuid());
        Cache(b.Local, "s1", user, Guid.NewGuid());
        Cache(b.Local, "s2", Guid.NewGuid(), tenant);

        a.Shared.EvictUser(user);
        a.Shared.EvictTenant(tenant);

        await Eventually(() => !b.Local.TryGet<object>("s1", out _) && !b.Local.TryGet<object>("s2", out _),
            "user or tenant eviction on A did not reach B");
    }

    // ---- permission versions --------------------------------------------------------------------

    [Fact]
    public async Task PermissionChangeOnA_MakesOlderSessionsStaleOnB()
    {
        var a = await StartInstanceAsync();
        var b = await StartInstanceAsync();
        var versionsA = new RedisPermissionVersionService(await redis.ConnectAsync(), a.Shared);
        var versionsB = new RedisPermissionVersionService(await redis.ConnectAsync(), b.Shared);
        var user = Guid.NewGuid();
        var sessionVersion = await versionsB.GetCurrentVersionAsync(user);

        await versionsA.IncrementVersionAsync(user);

        Assert.True(await versionsB.GetCurrentVersionAsync(user) > sessionVersion);
    }

    // ---- admin MFA challenges -------------------------------------------------------------------

    private async Task<RedisPlatformMfaChallengeStore> MfaStoreAsync()
        => new(await redis.ConnectAsync(), new SecureTokenGenerator(), new SystemDateTimeProvider());

    [Fact]
    public async Task MfaChallenge_CreatedOnA_IsReadAndConsumedOnB_Once()
    {
        var a = await MfaStoreAsync();
        var b = await MfaStoreAsync();
        var user = Guid.NewGuid();
        var challenge = await a.CreateForGoogleLinkAsync(user, "google-sub-1", TimeSpan.FromMinutes(5));

        var read = await b.GetAsync(challenge);
        Assert.Equal(user, read!.PlatformUserId);
        Assert.Equal("google-sub-1", read.PendingGoogleSub);

        var consumers = Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? a : b).TryConsumeAsync(challenge));
        var results = await Task.WhenAll(consumers);

        Assert.Single(results, r => r is not null);
        Assert.Null(await a.GetAsync(challenge));
    }

    [Fact]
    public async Task MfaFailedAttempts_AreCountedAcrossInstances()
    {
        var a = await MfaStoreAsync();
        var b = await MfaStoreAsync();
        var challenge = await a.CreateAsync(Guid.NewGuid(), TimeSpan.FromMinutes(5));

        Assert.True(await a.RegisterFailedAttemptAsync(challenge, maximumAttempts: 3));
        Assert.True(await b.RegisterFailedAttemptAsync(challenge, maximumAttempts: 3));
        Assert.False(await a.RegisterFailedAttemptAsync(challenge, maximumAttempts: 3));

        Assert.Null(await b.GetAsync(challenge));
    }

    [Fact]
    public async Task MfaChallenge_ExpiresWithItsLifetime()
    {
        var a = await MfaStoreAsync();
        var challenge = await a.CreateAsync(Guid.NewGuid(), TimeSpan.FromSeconds(1));

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Null(await a.GetAsync(challenge));
    }

    // ---- sign-in attempt counter ----------------------------------------------------------------

    [Fact]
    public async Task SignInAttemptsOnBothInstances_ShareOneCount_AndTheWindowResets()
    {
        var a = new RedisAttemptCounter(await redis.ConnectAsync(), new MemoryAttemptCounter(), NullLogger<RedisAttemptCounter>.Instance);
        var b = new RedisAttemptCounter(await redis.ConnectAsync(), new MemoryAttemptCounter(), NullLogger<RedisAttemptCounter>.Instance);
        var key = $"login:{Guid.NewGuid():N}";
        var window = TimeSpan.FromSeconds(1);

        Assert.Equal(1, await a.IncrementAsync(key, window, CancellationToken.None));
        Assert.Equal(2, await b.IncrementAsync(key, window, CancellationToken.None));
        Assert.Equal(3, await a.IncrementAsync(key, window, CancellationToken.None));

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.Equal(1, await b.IncrementAsync(key, window, CancellationToken.None));
    }

    [Fact]
    public async Task RedisDown_AttemptsAreStillCountedLocally()
    {
        var unreachable = await ConnectionMultiplexer.ConnectAsync(
            SharedStateRegistration.RedisOptions("127.0.0.1:1,connectTimeout=200,syncTimeout=200,asyncTimeout=200"));
        var counter = new RedisAttemptCounter(unreachable, new MemoryAttemptCounter(), NullLogger<RedisAttemptCounter>.Instance);

        Assert.Equal(1, await counter.IncrementAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal(2, await counter.IncrementAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    // ---- presence -------------------------------------------------------------------------------

    private async Task<RedisPresenceStore> PresenceAsync(TimeSpan? lifetime = null)
        => new(await redis.ConnectAsync(), NullLogger<RedisPresenceStore>.Instance, lifetime);

    [Fact]
    public async Task TrayConnectedToA_IsPresentOnB_UntilItDisconnects()
    {
        var trackerA = new TrayConnectionTracker(await PresenceAsync());
        var trackerB = new TrayConnectionTracker(await PresenceAsync());
        var (tenant, user) = (Guid.NewGuid(), Guid.NewGuid());

        trackerA.Connected(tenant, user, "conn-a");
        await Eventually(() => trackerB.IsConnected(tenant, user), "a tray connected to A was not present on B");

        trackerA.Disconnected(tenant, user, "conn-a");
        await Eventually(() => !trackerB.IsConnected(tenant, user), "a tray that disconnected from A stayed present on B");
    }

    [Fact]
    public async Task TrayOfADeadInstance_DropsOutAfterItsLifetime_ButARefreshedOneStays()
    {
        var lifetime = TimeSpan.FromSeconds(2);
        var dead = await PresenceAsync(lifetime);
        var alive = await PresenceAsync(lifetime);
        var observer = new TrayConnectionTracker(await PresenceAsync(lifetime));
        var tenant = Guid.NewGuid();
        var (deadUser, liveUser) = (Guid.NewGuid(), Guid.NewGuid());

        dead.TrayConnected(tenant, deadUser, "dead-conn"); // never disconnects, never refreshes
        alive.TrayConnected(tenant, liveUser, "live-conn");
        await Eventually(() => observer.IsConnected(tenant, deadUser), "the connection was never visible");

        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            alive.RefreshTrayConnections([(tenant, liveUser, "live-conn")]);
        }

        Assert.False(observer.IsConnected(tenant, deadUser));
        Assert.True(observer.IsConnected(tenant, liveUser));
    }

    [Fact]
    public async Task RedisDown_TrayCheckSaysNotConnected_SoTheHeartbeatFallbackDecides()
    {
        var unreachable = await ConnectionMultiplexer.ConnectAsync(
            SharedStateRegistration.RedisOptions("127.0.0.1:1,connectTimeout=200,syncTimeout=200,asyncTimeout=200"));
        var tracker = new TrayConnectionTracker(new RedisPresenceStore(unreachable, NullLogger<RedisPresenceStore>.Instance));

        Assert.False(tracker.IsConnected(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task WebActivityOnA_IsSeenOnB()
    {
        var a = new ONEVO.Api.Middleware.WebActivityTracker(await PresenceAsync());
        var b = new ONEVO.Api.Middleware.WebActivityTracker(await PresenceAsync());
        var (tenant, user) = (Guid.NewGuid(), Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;

        a.Touch(tenant, user, at);

        await Eventually(() => b.LastSeenAt(tenant, user) is { } seen
            && Math.Abs((seen - at).TotalMilliseconds) < 1, "web activity on A was not seen on B");
    }

    // ---- data protection ------------------------------------------------------------------------

    private async Task<IDataProtector> ProtectorAsync(
        RedisKey? key = null, System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
    {
        var mux = await redis.ConnectAsync();
        var services = new ServiceCollection();
        var builder = services.AddDataProtection()
            .SetApplicationName(SharedStateExtensions.DataProtectionApplicationName)
            .PersistKeysToStackExchangeRedis(mux, key ?? SharedStateExtensions.DataProtectionKey);
        if (certificate is not null)
            builder.ProtectKeysWithCertificate(certificate);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "TenantScheme", "v2");
    }

    [Fact]
    public async Task CookieProtectedByA_IsReadableByB()
    {
        var a = await ProtectorAsync();
        var b = await ProtectorAsync();

        var cookie = a.Protect("session-key-reference");

        Assert.Equal("session-key-reference", b.Unprotect(cookie));
    }

    [Fact]
    public async Task EncryptedKeyRing_IsUnreadableInRedis_ButWorksAcrossInstances()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=crownus-dataprotection-test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Round-trip through PFX the way the setting delivers it, so the private key is usable for decryption.
        var pfx = Convert.ToBase64String(created.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "pw"));
        var certificate = SharedStateExtensions.LoadKeyEncryptionCertificate(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SharedStateExtensions.KeyEncryptionCertificateKey] = pfx,
                [SharedStateExtensions.KeyEncryptionCertificatePasswordKey] = "pw",
            }).Build())!;
        var key = new RedisKey($"crownus:test-encrypted-ring:{Guid.NewGuid():N}");

        var a = await ProtectorAsync(key, certificate);
        var b = await ProtectorAsync(key, certificate);
        var cookie = a.Protect("session-key-reference");

        Assert.Equal("session-key-reference", b.Unprotect(cookie));
        var stored = string.Join("", (await (await redis.ConnectAsync()).GetDatabase().ListRangeAsync(key)).Select(v => v.ToString()));
        Assert.Contains("EncryptedData", stored);
        Assert.DoesNotContain("<masterKey", stored);
    }
}
