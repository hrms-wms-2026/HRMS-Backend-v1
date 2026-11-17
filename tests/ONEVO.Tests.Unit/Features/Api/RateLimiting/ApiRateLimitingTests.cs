using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Api.RateLimiting;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.RateLimiting;

/// <summary>
/// Task 07: the global limiter runs after authentication and partitions by identity. These tests
/// run the real AddApiRateLimiting/UseApiRateLimiting pipeline on a TestServer with header-driven
/// stand-ins for the three real authentication schemes.
/// </summary>
public sealed class ApiRateLimitingTests
{
    private const string OfficeIp = "203.0.113.10";
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public async Task TwoUsersOnOneIp_DoNotBlockEachOther()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:SignedIn:TokenLimit"] = "5",
            ["RateLimiting:SignedIn:TokensPerPeriod"] = "1",
            ["RateLimiting:SignedIn:ReplenishmentPeriodSeconds"] = "60",
        });
        var client = app.GetTestClient();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp, user: alice)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: alice)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "alice used her whole bucket");
        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: bob)).StatusCode
            .Should().Be(HttpStatusCode.OK, "bob has his own bucket even on the same IP");
    }

    [Fact]
    public async Task FiftySignedInUsersOnOneIp_CanEachLoadTheDashboard_WithDefaultLimits()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var users = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        // A dashboard load is ~15 parallel requests; all 50 office users load at once.
        var requests = users.SelectMany(user => Enumerable.Range(0, 15)
            .Select(_ => SendAsync(client, "/api/v1/ping", OfficeIp, user: user)));
        var responses = await Task.WhenAll(requests);

        responses.Should().HaveCount(750);
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousFloodFromOneIp_IsBlocked_WithRetryAfterAndProblemBody()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var limit = app.Services.GetRequiredService<IRateLimitSettingsProvider>()
            .GetSettings(null).Settings.Anonymous.PermitLimit;

        for (var i = 0; i < limit; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await SendAsync(client, "/api/v1/ping", OfficeIp);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        int.Parse(retryAfter!.Single()).Should().BePositive();
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("type").GetString().Should().Be("urn:crownus:error:rate-limit");
        body.GetProperty("title").GetString().Should().Be("Too Many Requests");
        body.GetProperty("status").GetInt32().Should().Be(429);

        (await SendAsync(client, "/api/v1/ping", "198.51.100.7")).StatusCode
            .Should().Be(HttpStatusCode.OK, "another IP is a different anonymous partition");
        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: Guid.NewGuid())).StatusCode
            .Should().Be(HttpStatusCode.OK, "a signed-in user on the flooded IP is not affected");
    }

    [Fact]
    public async Task TrayDevices_ArePartitionedPerDevice()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:TrayDevice:TokenLimit"] = "3",
            ["RateLimiting:TrayDevice:TokensPerPeriod"] = "1",
            ["RateLimiting:TrayDevice:ReplenishmentPeriodSeconds"] = "60",
        });
        var client = app.GetTestClient();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp, device: deviceA)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(client, "/api/v1/ping", OfficeIp, device: deviceA)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, "/api/v1/ping", OfficeIp, device: deviceB)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantCeiling_IsChainedAcrossUsersOfOneTenant()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:TenantCeiling:TokenLimit"] = "4",
            ["RateLimiting:TenantCeiling:TokensPerPeriod"] = "1",
            ["RateLimiting:TenantCeiling:ReplenishmentPeriodSeconds"] = "60",
        });
        var client = app.GetTestClient();

        for (var i = 0; i < 4; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp, user: Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: Guid.NewGuid())).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "the tenant ceiling is shared by every user of the tenant");
        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: Guid.NewGuid(), tenant: Guid.NewGuid())).StatusCode
            .Should().Be(HttpStatusCode.OK, "another tenant has its own ceiling");
    }

    [Fact]
    public async Task ChangedSetting_AppliesOnTheNextRequest_WithoutRestart()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var user = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp, user: user)).StatusCode.Should().Be(HttpStatusCode.OK);

        var configuration = (IConfigurationRoot)app.Services.GetRequiredService<IConfiguration>();
        configuration["RateLimiting:SignedIn:TokenLimit"] = "2";
        configuration["RateLimiting:SignedIn:TokensPerPeriod"] = "1";
        configuration["RateLimiting:SignedIn:ReplenishmentPeriodSeconds"] = "60";
        configuration.Reload();

        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: user)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: user)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "/api/v1/ping", OfficeIp, user: user)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "the new 2-request bucket applies at once");
    }

    [Fact]
    public async Task InvalidReloadedSetting_KeepsLastGoodLimits()
    {
        await using var app = await StartAsync();
        var provider = app.Services.GetRequiredService<IRateLimitSettingsProvider>();
        var before = provider.GetSettings(null);

        var configuration = (IConfigurationRoot)app.Services.GetRequiredService<IConfiguration>();
        configuration["RateLimiting:SignedIn:TokenLimit"] = "0";
        configuration.Reload();

        var after = provider.GetSettings(null);
        after.Version.Should().Be(before.Version);
        after.Settings.SignedIn.TokenLimit.Should().Be(before.Settings.SignedIn.TokenLimit);
    }

    [Fact]
    public async Task HealthEndpoints_AreExempt()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:Anonymous:PermitLimit"] = "1",
        });
        var client = app.GetTestClient();

        for (var i = 0; i < 20; i++)
        {
            (await SendAsync(client, "/health", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await SendAsync(client, "/health/ready", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AdminRoutes_UseTheirOwnRule_PartitionedByAdminUser()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:AdminAnonymous:PermitLimit"] = "2",
            ["RateLimiting:AdminSignedIn:TokenLimit"] = "3",
            ["RateLimiting:AdminSignedIn:TokensPerPeriod"] = "1",
            ["RateLimiting:AdminSignedIn:ReplenishmentPeriodSeconds"] = "60",
        });
        var client = app.GetTestClient();
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
            (await SendAsync(client, "/admin/v1/ping", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "/admin/v1/ping", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, "/api/v1/ping", OfficeIp)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the tenant API anonymous rule is separate from the admin one");

        for (var i = 0; i < 3; i++)
            (await SendAsync(client, "/admin/v1/ping", OfficeIp, admin: adminA)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "/admin/v1/ping", OfficeIp, admin: adminA)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, "/admin/v1/ping", OfficeIp, admin: adminB)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RoutesOutsideTheApi_AreNotLimited()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:Anonymous:PermitLimit"] = "1",
        });
        var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
            (await SendAsync(client, "/swagger/ping", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabled_LetsEverythingThrough()
    {
        await using var app = await StartAsync(new()
        {
            ["RateLimiting:Enabled"] = "false",
            ["RateLimiting:Anonymous:PermitLimit"] = "1",
        });
        var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
            (await SendAsync(client, "/api/v1/ping", OfficeIp)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void PartitionKeys_CarryTheSettingsVersion()
    {
        var user = Guid.NewGuid();
        var subject = RateLimitSubject.ForUser(TenantA, user);

        subject.PartitionKey(7).Should().Be($"user:{TenantA}:{user}:v7");
        RateLimitSubject.ForAnonymous("10.1.2.3").PartitionKey(3).Should().Be("anon:10.1.2.3:v3");
        RateLimitSubject.ForTenantCeiling(TenantA, 3).Should().Be($"tenant:{TenantA}:v3");
    }

    // ── host ────────────────────────────────────────────────────────────────────

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

        builder.Services.AddAuthentication(RateLimitAuthenticationSchemes.Tenant)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(RateLimitAuthenticationSchemes.Tenant, _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(RateLimitAuthenticationSchemes.Admin, _ => { })
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(RateLimitAuthenticationSchemes.TrayDevice, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddApiRateLimiting(builder.Configuration);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-IP", out var ip))
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip!);
            return next(context);
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseApiRateLimiting();
        app.UseAuthorization();
        app.MapGet("/api/v1/ping", () => "ok");
        app.MapGet("/admin/v1/ping", () => "ok");
        app.MapGet("/health", () => "ok");
        app.MapGet("/health/ready", () => "ok");
        app.MapGet("/swagger/ping", () => "ok");

        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string path, string ip,
        Guid? user = null, Guid? tenant = null, Guid? device = null, Guid? admin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-IP", ip);
        if (user is Guid u)
            request.Headers.Add("X-Test-User", $"{tenant ?? TenantA}:{u}");
        if (device is Guid d)
        {
            request.Headers.Add("X-Test-Device", $"{tenant ?? TenantA}:{d}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test");
        }
        if (admin is Guid a)
            request.Headers.Add("X-Test-Admin", a.ToString());
        return client.SendAsync(request);
    }

    /// <summary>Authenticates from test headers; the header it reads depends on the scheme name.</summary>
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = Scheme.Name switch
            {
                RateLimitAuthenticationSchemes.Tenant when Read("X-Test-User") is [var t, var u] =>
                    new[] { new Claim(ClaimTypes.NameIdentifier, u), new Claim("tenant_id", t), new Claim("session_id", Guid.NewGuid().ToString()) },
                RateLimitAuthenticationSchemes.TrayDevice when Read("X-Test-Device") is [var t, var d] =>
                    new[] { new Claim(ClaimTypes.NameIdentifier, d), new Claim("tenant_id", t), new Claim("token_type", "tray_device") },
                RateLimitAuthenticationSchemes.Admin when Request.Headers["X-Test-Admin"].FirstOrDefault() is { } a =>
                    new[] { new Claim(ClaimTypes.NameIdentifier, a) },
                _ => null,
            };

            return Task.FromResult(claims is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }

        private string[]? Read(string header) =>
            Request.Headers[header].FirstOrDefault()?.Split(':');
    }
}
