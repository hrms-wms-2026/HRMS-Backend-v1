using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Api.Hubs;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SharedState;

/// <summary>
/// Task 21: two real API hosts, one database, one Redis (Redis__ConnectionString set). Checks the wiring in
/// Program.cs and AddInfrastructure, not only the Redis classes: shared data-protection keys (the sign-in cookie),
/// session evictions, tray presence and the SignalR backplane.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TwoInstanceApiTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string RedisSetting = "Redis__ConnectionString";
    private IntegrationTestEnvironmentScope _environment = null!;
    private ONEVO.Tests.Integration.ApiBootTestFactory _a = null!;
    private ONEVO.Tests.Integration.ApiBootTestFactory _b = null!;
    private string? _previousRedis;

    public async Task InitializeAsync()
    {
        _previousRedis = Environment.GetEnvironmentVariable(RedisSetting);
        Environment.SetEnvironmentVariable(RedisSetting, redis.ConnectionString);
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environment = new IntegrationTestEnvironmentScope(connectionString);
        _a = new ONEVO.Tests.Integration.ApiBootTestFactory(connectionString);
        _b = new ONEVO.Tests.Integration.ApiBootTestFactory(connectionString);
        // Start both hosts (hosted services subscribe to Redis on start).
        _a.CreateClient();
        _b.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _a.DisposeAsync();
        await _b.DisposeAsync();
        await _environment.DisposeAsync();
        Environment.SetEnvironmentVariable(RedisSetting, _previousRedis);
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task BothInstances_AreReady_WithRedisConnected()
    {
        var response = await _b.CreateClient().GetAsync("/health/ready");

        Assert.True(response.IsSuccessStatusCode, $"/health/ready returned {(int)response.StatusCode}");
    }

    [Fact]
    public void CookieProtectedByA_IsReadableByB()
    {
        string[] purpose = ["Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "TenantScheme", "v2"];
        var a = _a.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose[0], purpose[1..]);
        var b = _b.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose[0], purpose[1..]);

        Assert.Equal("ticket-reference", b.Unprotect(a.Protect("ticket-reference")));
    }

    [Fact]
    public async Task UserEvictedOnA_IsNoLongerServedFromBsTicketCache()
    {
        var cacheB = _b.Services.GetRequiredService<SessionTicketCache>();
        var user = Guid.NewGuid();
        cacheB.Set("two-host-session", new object(), user, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMilliseconds(-10));
        Assert.True(cacheB.TryGet<object>("two-host-session", out _));

        _a.Services.GetRequiredService<ISessionTicketCache>().EvictUser(user);

        await Eventually(() => !cacheB.TryGet<object>("two-host-session", out _),
            "a user evicted on instance A was still served from instance B's ticket cache");
    }

    [Fact]
    public async Task TrayConnectedToA_IsPresentOnB()
    {
        var (tenant, user) = (Guid.NewGuid(), Guid.NewGuid());

        _a.Services.GetRequiredService<TrayConnectionTracker>().Connected(tenant, user, "two-host-conn");

        var presenceB = _b.Services.GetRequiredService<ITrayConnectionPresence>();
        await Eventually(() => presenceB.IsConnected(tenant, user), "a tray connected to A was not present on B");
    }

    [Fact]
    public void BothInstances_UseTheRedisSignalRBackplane()
    {
        foreach (var host in new[] { _a, _b })
        {
            var manager = host.Services.GetRequiredService<HubLifetimeManager<InboxHub>>();
            Assert.Contains("Redis", manager.GetType().Name, StringComparison.Ordinal);
        }
    }
}
