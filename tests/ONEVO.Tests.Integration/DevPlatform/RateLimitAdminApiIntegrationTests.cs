using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.DevPlatform;

/// <summary>
/// Improvements task 47 on SQL Server: the rate limit admin routes need the right platform
/// permission, every save writes one history row, a stale rowVersion gets 409, and a saved change
/// reaches the limiter's settings on this instance straight away.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class RateLimitAdminApiIntegrationTests : IAsyncLifetime
{
    private const string Reason = "Customer import week, approved by ops.";
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _anonymous = null!;
    private Guid _tenantId;
    private Guid _planId;

    public async Task InitializeAsync()
    {
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _factory = new AdminTestFactory(connectionString);
        var options = new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false };
        _client = _factory.CreateClient(options);
        _anonymous = _factory.CreateClient(options);

        var session = await PlatformAdminTestSignIn.SignInAsync(_client, "test_admin@onevo.dev", "test_password_123");
        _client.DefaultRequestHeaders.Add("Cookie", session.SessionCookie);
        _client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        (_tenantId, _planId) = await SeedTenantAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _anonymous.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    private async Task<(Guid TenantId, Guid PlanId)> SeedTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Name = "Rate Limit Plan", Code = "rate-limit-plan", Tier = "standard",
            IncludedModulesJson = "[]", CompanySizeRange = "11-50", CalculatedMonthlyPrice = 1m,
            CalculatedAnnualPrice = 12m, Currency = "USD", IsActive = true,
        };
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Rate Limit Tenant", Slug = "rate-limit-tenant",
            IndustryProfile = "professional_services", CompanySizeRange = "11-50",
            Status = TenantStatus.Active, SubscriptionPlanId = plan.Id, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SubscriptionPlans.Add(plan);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return (tenant.Id, plan.Id);
    }

    private string GlobalRoute => "/admin/v1/system-config/rate-limits";
    private string PlanRoute => $"/admin/v1/subscription-plans/{_planId}/rate-limits";
    private string TenantRoute => $"/admin/v1/tenants/{_tenantId}/rate-limits";

    private IEnumerable<string> ReadRoutes() =>
        [GlobalRoute, $"{GlobalRoute}/changes", $"{GlobalRoute}/rejections", PlanRoute, TenantRoute];

    private IEnumerable<(HttpMethod Method, string Path)> WriteRoutes() =>
    [
        (HttpMethod.Put, $"{GlobalRoute}/SignedIn"), (HttpMethod.Delete, $"{GlobalRoute}/SignedIn"),
        (HttpMethod.Put, $"{PlanRoute}/SignedIn"), (HttpMethod.Delete, $"{PlanRoute}/SignedIn"),
        (HttpMethod.Put, $"{TenantRoute}/SignedIn"), (HttpMethod.Delete, $"{TenantRoute}/SignedIn"),
    ];

    private static HttpRequestMessage Write(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body ?? new { perMinute = 900, burst = 150, reason = Reason });
        return request;
    }

    private static object SetBody(int perMinute, int? burst, string? rowVersion = null) =>
        new { perMinute = perMinute, burst, reason = Reason, rowVersion = rowVersion };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Gives the signed-in admin a role with only these permissions (resolved per request).</summary>
    private async Task GrantOnlyAsync(params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assignment = await db.PlatformUserRoles.SingleAsync();
        var role = new PlatformRole { Id = Guid.NewGuid(), Name = $"Rate limit test {Guid.NewGuid():N}", IsActive = true };
        db.PlatformRoles.Add(role);
        foreach (var code in permissions)
            db.PlatformRolePermissions.Add(new PlatformRolePermission { RoleId = role.Id, PermissionCode = code });
        db.PlatformUserRoles.Remove(assignment);
        db.PlatformUserRoles.Add(new PlatformUserRole { UserId = assignment.UserId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------- authorization

    [Fact]
    public async Task Anonymous_call_gets_401_on_every_route()
    {
        foreach (var path in ReadRoutes())
            (await _anonymous.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        foreach (var (method, path) in WriteRoutes())
            (await _anonymous.SendAsync(Write(method, path))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path}");
    }

    [Fact]
    public async Task Without_read_permission_every_GET_is_403()
    {
        await GrantOnlyAsync(PlatformPermissionCatalog.TenantsRead);

        foreach (var path in ReadRoutes())
            (await _client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task Read_without_manage_can_GET_but_every_PUT_and_DELETE_is_403()
    {
        await GrantOnlyAsync(PlatformPermissionCatalog.SystemConfigRead);

        foreach (var path in ReadRoutes())
            (await _client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
        foreach (var (method, path) in WriteRoutes())
            (await _client.SendAsync(Write(method, path))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}");
    }

    [Fact]
    public async Task Unknown_limit_name_returns_400()
    {
        foreach (var name in new[] { "login", "Enabled", "PreAuthentication" })
            (await _client.SendAsync(Write(HttpMethod.Put, $"{GlobalRoute}/{name}"))).StatusCode
                .Should().Be(HttpStatusCode.BadRequest, name);
    }

    // ------------------------------------------------------------- writes

    [Fact]
    public async Task Successful_PUT_writes_exactly_one_history_row_with_actor_old_and_new_values()
    {
        var created = await _client.SendAsync(Write(HttpMethod.Put, $"{GlobalRoute}/SignedIn", SetBody(900, 150)));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var signedIn = (await JsonAsync(created)).EnumerateArray().Single(e => e.GetProperty("limitName").GetString() == "SignedIn");
        var rowVersion = signedIn.GetProperty("override").GetProperty("rowVersion").GetString();
        signedIn.GetProperty("effective").GetProperty("perMinute").GetInt32().Should().Be(900);

        var updated = await _client.SendAsync(Write(HttpMethod.Put, $"{GlobalRoute}/SignedIn", SetBody(1200, 200, rowVersion)));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = (await db.PlatformUserRoles.SingleAsync()).UserId;
        var changes = await db.RateLimitSettingChanges.AsNoTracking().OrderBy(c => c.ChangedAt).ToListAsync();
        changes.Should().HaveCount(2);
        changes.Should().AllSatisfy(c =>
        {
            c.PlatformUserId.Should().Be(actor);
            c.Reason.Should().Be(Reason);
            c.CorrelationId.Should().NotBeNullOrEmpty();
        });
        (changes[0].OldPerMinute, changes[0].NewPerMinute, changes[0].NewBurst).Should().Be(((int?)null, 900, 150));
        (changes[1].OldPerMinute, changes[1].OldBurst, changes[1].NewPerMinute, changes[1].NewBurst)
            .Should().Be((900, 150, 1200, 200));

        var history = await JsonAsync(await _client.GetAsync($"{GlobalRoute}/changes?scope=global&page=1&pageSize=1"));
        history.GetProperty("totalCount").GetInt32().Should().Be(2);
        history.GetProperty("items").EnumerateArray().Single().GetProperty("newPerMinute").GetInt32().Should().Be(1200);
    }

    [Fact]
    public async Task Same_value_PUT_writes_no_history()
    {
        var first = await JsonAsync(await _client.SendAsync(Write(HttpMethod.Put, $"{TenantRoute}/TrayDevice", SetBody(600, 120))));
        var rowVersion = first.GetProperty("limits").EnumerateArray()
            .Single(e => e.GetProperty("limitName").GetString() == "TrayDevice")
            .GetProperty("override").GetProperty("rowVersion").GetString();

        (await _client.SendAsync(Write(HttpMethod.Put, $"{TenantRoute}/TrayDevice", SetBody(600, 120, rowVersion))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.RateLimitSettingChanges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PUT_with_a_stale_row_version_returns_409()
    {
        var created = await JsonAsync(await _client.SendAsync(Write(HttpMethod.Put, $"{PlanRoute}/SignedIn", SetBody(900, 150))));
        var stale = created.GetProperty("limits").EnumerateArray()
            .Single(e => e.GetProperty("limitName").GetString() == "SignedIn")
            .GetProperty("override").GetProperty("rowVersion").GetString();

        // The first admin saves; the second still holds the old version.
        (await _client.SendAsync(Write(HttpMethod.Put, $"{PlanRoute}/SignedIn", SetBody(1200, 150, stale))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await _client.SendAsync(Write(HttpMethod.Put, $"{PlanRoute}/SignedIn", SetBody(1500, 150, stale)));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Tenant_PUT_reaches_the_limiter_settings_on_this_instance_at_once()
    {
        var provider = _factory.Services.GetRequiredService<IRateLimitSettingsProvider>();
        var before = provider.GetSettings(_tenantId);

        var response = await _client.SendAsync(Write(HttpMethod.Put, $"{TenantRoute}/SignedIn", SetBody(1200, 40)));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // No wait for the 30-second check: the command invalidated the provider after commit.
        var after = provider.GetSettings(_tenantId);
        after.Version.Should().BeGreaterThan(before.Version);
        after.Settings.SignedIn.TokenLimit.Should().Be(40);
        after.Settings.SignedIn.TokensPerPeriod.Should().Be(1200 * after.Settings.SignedIn.ReplenishmentPeriodSeconds / 60);
        provider.GetSettings(null).Settings.SignedIn.TokenLimit.Should().Be(before.Settings.SignedIn.TokenLimit);
    }

    [Fact]
    public async Task DELETE_with_reason_in_the_query_removes_the_override_and_records_it()
    {
        var created = await JsonAsync(await _client.SendAsync(Write(HttpMethod.Put, $"{GlobalRoute}/Anonymous", SetBody(600, null))));
        var rowVersion = created.EnumerateArray().Single(e => e.GetProperty("limitName").GetString() == "Anonymous")
            .GetProperty("override").GetProperty("rowVersion").GetString();

        var removed = await _client.DeleteAsync(
            $"{GlobalRoute}/Anonymous?reason={Uri.EscapeDataString(Reason)}&rowVersion={rowVersion}");

        removed.StatusCode.Should().Be(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        var anonymous = (await JsonAsync(removed)).EnumerateArray().Single(e => e.GetProperty("limitName").GetString() == "Anonymous");
        anonymous.GetProperty("effectiveSource").GetString().Should().Be("appsettings");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.RateLimitSettings.CountAsync()).Should().Be(0);
        (await db.RateLimitSettingChanges.CountAsync(c => c.NewPerMinute == null)).Should().Be(1);
    }

    [Fact]
    public async Task Plan_detail_includes_a_read_only_rate_limit_summary()
    {
        var plan = await JsonAsync(await _client.GetAsync($"/admin/v1/subscription-plans/{_planId}"));

        plan.GetProperty("rateLimits").EnumerateArray().Select(e => e.GetProperty("limitName").GetString())
            .Should().Equal("SignedIn", "TrayDevice", "TenantCeiling");
    }

    // ------------------------------------------------------------- task 49: rejections

    [Fact]
    public async Task Rejections_put_the_most_limited_tenant_on_top_with_its_name_and_effective_limits()
    {
        // The limiter -> stats wiring is covered on a TestServer (ApiRateLimitingTests); there is no
        // tenant sign-in helper here, so the rejections are recorded on the instance's own counter.
        var stats = _factory.Services.GetRequiredService<IRateLimitRejectionStats>();
        for (var i = 0; i < 25; i++)
            stats.Record(_tenantId, "User");
        stats.Record(_tenantId, "TrayDevice");
        stats.Record(null, "Anonymous");
        (await _client.SendAsync(Write(HttpMethod.Put, $"{TenantRoute}/SignedIn", SetBody(1200, 200)))).EnsureSuccessStatusCode();

        var body = await JsonAsync(await _client.GetAsync($"{GlobalRoute}/rejections?minutes=60"));

        body.GetProperty("scope").GetString().Should().Be("instance");
        body.GetProperty("note").GetString().Should().Contain("this API server only");
        body.GetProperty("minutes").GetInt32().Should().Be(60);
        var top = body.GetProperty("tenants").EnumerateArray().First();
        top.GetProperty("tenantId").GetGuid().Should().Be(_tenantId);
        top.GetProperty("tenantName").GetString().Should().Be("Rate Limit Tenant");
        top.GetProperty("totalRejections").GetInt64().Should().Be(26);
        top.GetProperty("byKind").EnumerateArray().First().GetProperty("kind").GetString().Should().Be("User");
        var signedIn = top.GetProperty("effectiveLimits").EnumerateArray()
            .Single(l => l.GetProperty("limitName").GetString() == "SignedIn");
        signedIn.GetProperty("perMinute").GetInt32().Should().Be(1200);
        signedIn.GetProperty("burst").GetInt32().Should().Be(200);

        var raw = await (await _client.GetAsync($"{GlobalRoute}/rejections")).Content.ReadAsStringAsync();
        raw.Should().NotContainAny("userId", "ipAddress", "sessionId");
    }

    [Fact]
    public async Task Rejections_minutes_outside_1_to_60_is_400()
    {
        (await _client.GetAsync($"{GlobalRoute}/rejections?minutes=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync($"{GlobalRoute}/rejections?minutes=61")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
