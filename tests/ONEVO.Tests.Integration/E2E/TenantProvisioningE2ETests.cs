using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.E2E;

/// <summary>
/// Full tenant-provisioning journey against a real PostgreSQL database:
/// admin creates a tenant with an owner invite, the owner accepts the invite,
/// logs in with the resulting session cookie, and uses tenant APIs under CSRF
/// and host-isolation rules.
///
/// Database resolution: set ONEVO_TEST_DB to a PostgreSQL connection string to
/// run against a local server (no Docker needed); otherwise a Testcontainers
/// instance is started.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class TenantProvisioningE2ETests : IAsyncLifetime
{
    private const string Slug = "demo-e2e";
    private const string TenantHost = Slug + ".localhost";
    private const string AdminHost = "admin.localhost";
    private const string OwnerEmail = "owner@demo-e2e.test";
    private const string OwnerPassword = "OwnerPass@2026!";

    /// <summary>Seeded by SeedPhaseOnePlanModules / model HasData.</summary>
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_e2e_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();
        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login", new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var cookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = cookies["admin_csrf"];
        _adminCookie = $"admin_session={cookies["admin_session"]}";
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Seeded_starter_plan_includes_exactly_the_canonical_phase1_product_modules()
    {
        // Regression guard: see PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md.
        // included_modules_json for this exact plan (SeededPlanId) must equal exactly the
        // canonical Phase 1 HR / Workforce Intelligence / WorkSync product package module
        // keys - never platform capability keys ("auth"/"configuration"/"roles"/
        // "notifications") and never the old mixed/aliased keys ("org"/"monitoring"/
        // "workforce"/"verification"/"exceptions"/"analytics"/"work_management"/"chat"/
        // "chat_ai"/"integrations"/"workflow_engine"). roles:read/roles:manage still reach
        // the tenant Owner - not via this list, but via DefaultRoleSeeder's unconditional
        // platform-baseline permission grant (see DefaultRoleSeederTests). org:read/
        // org:manage reach the Owner because "org_structure" is a genuine product module
        // in this list.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
            .SingleAsync(p => p.Id == SeededPlanId);

        var modules = plan.GetIncludedModules();

        modules.Should().BeEquivalentTo(
        [
            "org_structure", "core_hr", "leave", "calendar", "time_attendance",
            "activity_monitoring", "discrepancy_engine", "identity_verification",
            "exception_engine", "productivity_analytics", "desktop_agent_gateway",
            "worksync_foundation", "projects", "objectives_milestones", "tasks", "boards",
            "planning_sprints"
        ]);

        modules.Should().NotContain(["auth", "configuration", "roles", "notifications"]);
        modules.Should().NotContain([
            "org", "monitoring", "workforce", "verification", "exceptions", "analytics",
            "work_management", "chat", "chat_ai", "integrations", "workflow_engine"
        ]);
    }

    [Fact]
    public async Task Full_tenant_provisioning_flow()
    {
        // ── 1. Admin creates a tenant draft with an owner invite ───────────────
        var tenantId = await CreateTenantAsync();

        // ── 2. The invite email is delivered asynchronously by the outbox worker;
        //       the plaintext token is only ever emitted inside that email ───────
        var inviteToken = await WaitForInviteTokenAsync();
        inviteToken.Should().NotBeNullOrEmpty("the owner invite email must carry the plaintext token");

        // ── 3. Invitee inspects the invitation on the tenant host ──────────────
        var detail = await GetJsonAsync(TenantHost, $"/api/v1/auth/invitations/{inviteToken}");
        detail.GetProperty("status").GetString().Should().Be("pending");
        detail.GetProperty("invited_email").GetString().Should().Be(OwnerEmail);

        // ── 4. Invitee accepts with a password; a session is established ───────
        var (acceptBody, acceptCookies) = await PostJsonAsync(
            TenantHost,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = OwnerPassword,
                confirm_password = OwnerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });

        acceptBody.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        acceptCookies.Should().ContainKey("onevo_session");
        acceptCookies.Should().ContainKey("onevo_csrf");

        // ── 5. Provisioning confirm activates the tenant for real ──────────────
        // subscription / modules / settings / roles are all seeded by tenant
        // creation, and the owner invite was just accepted above, so the
        // activation guard is satisfied without any direct DB write.
        var confirm = await SendAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm",
            new { confirm = true }, cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertTenantStatusAsync(tenantId, TenantStatus.Trial);

        // ── 6. Owner logs in via base-domain credential-first login ────────────
        // Tenant-host password login is retired; the base host resolves the single eligible
        // workspace from verified credentials. Invite completion already appended the current
        // required legal records before issuing its session, so this login can issue a session
        // directly (no legal/MFA challenge in between).
        const string BaseHost = "localhost";
        var rejectedTenantHostLogin = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        rejectedTenantHostLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var rejectedBody = await rejectedTenantHostLogin.Content.ReadAsStringAsync();
        rejectedBody.Should().Contain("Tenant-host password login is not supported.");
        rejectedTenantHostLogin.Headers.Contains("Set-Cookie").Should().BeFalse(
            "a rejected tenant-host password login must not issue any session cookie");

        // The base host never signs in directly, even once every gate clears - it hands off to the
        // tenant host via a one-time exchange code (TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md).
        var loginResponse = await SendAsync(HttpMethod.Post, BaseHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        var loginBody = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginBody.ToString());
        loginResponse.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse(
            "the base host must never set onevo_session/onevo_csrf");

        loginBody.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        loginBody.GetProperty("redirect_required").GetBoolean().Should().BeTrue();
        var continueUrl = new Uri(loginBody.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        continueUrl.Host.Should().Be(TenantHost);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(
            HttpMethod.Post, TenantHost, "/api/v1/auth/session-exchange", new { code = exchangeCode });
        var exchangeBody = await ReadJsonAsync(exchangeResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, exchangeBody.ToString());
        var cookies = ParseSetCookies(exchangeResponse);

        exchangeBody.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        cookies.Should().ContainKey("onevo_session");
        cookies.Should().ContainKey("onevo_csrf");

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}";
        var csrfCookieValue = cookies["onevo_csrf"];
        var csrfHeader = Uri.UnescapeDataString(csrfCookieValue);

        var me = await SendAsync(HttpMethod.Get, TenantHost, "/api/v1/auth/me",
            body: null, cookie: sessionCookie);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await ReadJsonAsync(me);
        meBody.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        meBody.GetProperty("user").TryGetProperty("tenant_id", out _).Should().BeFalse();
        meBody.GetProperty("user").TryGetProperty("user_id", out _).Should().BeFalse();

        // active_modules must reflect the tenant's canonical Phase 1 product modules and
        // must never expose platform capability keys - see
        // PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md.
        var activeModules = meBody.GetProperty("active_modules").EnumerateArray()
            .Select(m => m.GetString()).ToList();
        activeModules.Should().Contain(["org_structure", "core_hr", "leave", "calendar"]);
        activeModules.Should().NotContain(["auth", "configuration", "roles", "notifications"]);

        var removedRefresh = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/auth/refresh",
            body: null,
            cookie: $"{sessionCookie}; onevo_csrf={csrfCookieValue}",
            csrfToken: csrfHeader);
        removedRefresh.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        // ── 7. Authenticated tenant read works ─────────────────────────────────
        var roles = await GetJsonAsync(TenantHost, "/api/v1/roles", cookie: sessionCookie);
        var roleNames = roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        roleNames.Should().Contain("Owner");
        roleNames.Should().Contain("Employee");

        // ── 8. CSRF: mutating call without the header is rejected ──────────────
        var noCsrf = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/roles",
            new { name = "Blocked Role" },
            cookie: $"{sessionCookie}; onevo_csrf={csrfCookieValue}");
        noCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // ── 9. CSRF: with the matching header the write succeeds ───────────────
        var createRole = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/roles",
            new { name = "HR Manager", description = "created by E2E flow" },
            cookie: $"{sessionCookie}; onevo_csrf={csrfCookieValue}",
            csrfToken: csrfHeader);
        createRole.StatusCode.Should().Be(HttpStatusCode.Created);

        // ── 10. Host isolation both ways ────────────────────────────────────────
        var tenantApiOnAdminHost = await SendAsync(HttpMethod.Get, AdminHost, "/api/v1/roles",
            body: null, cookie: sessionCookie);
        tenantApiOnAdminHost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminApiOnTenantHost = await SendAsync(HttpMethod.Get, TenantHost, "/admin/v1/tenants",
            body: null, cookie: _adminCookie, csrfToken: _adminCsrfToken);
        adminApiOnTenantHost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var logout = await SendAsync(HttpMethod.Post, TenantHost, "/api/v1/auth/logout",
            body: null,
            cookie: $"{sessionCookie}; onevo_csrf={csrfCookieValue}",
            csrfToken: csrfHeader);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var meAfterRevocation = await SendAsync(HttpMethod.Get, TenantHost, "/api/v1/auth/me",
            body: null, cookie: sessionCookie);
        meAfterRevocation.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Flow steps ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreateTenantAsync()
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = CreateTenantBody(Slug);

        var response = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", body,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: idempotencyKey);

        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, json.ToString());
        json.GetProperty("status").GetString().Should().Be("provisioning");
        json.GetProperty("ownerInvite").GetProperty("deliveryStatus").GetString().Should().Be("queued");
        var tenantId = json.GetProperty("tenantId").GetGuid();

        // Idempotency: same key + same body replays the original response
        // instead of creating a second tenant.
        var replay = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", body,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: idempotencyKey);
        var replayJson = await ReadJsonAsync(replay);
        replayJson.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        replay.Headers.TryGetValues("Idempotency-Replayed", out _).Should().BeTrue();

        // Idempotency: same key + different body must be rejected.
        var mismatch = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants",
            CreateTenantBody("other-slug"), cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: idempotencyKey);
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);

        return tenantId;
    }

    private static object CreateTenantBody(string slug) => new
    {
        company_name = "Demo E2E Company",
        slug,
        industry_profile = "technology",
        company_size_range = "51-200",
        legal_entity_name = "Demo E2E (Pvt) Ltd",
        registration_number = "PV99999",
        country = "LK",
        timezone = "Asia/Colombo",
        currency = "LKR",
        subscription = new
        {
            plan_id = SeededPlanId,
            billing_cycle = "monthly",
            commercial_model = "standard"
        },
        owner_invite = new
        {
            email = OwnerEmail,
            first_name = "Demo",
            last_name = "Owner",
            completion_methods = new[] { "password" }
        }
    };

    private async Task<string?> WaitForInviteTokenAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var token = _email.LastInviteToken();
            if (!string.IsNullOrEmpty(token))
                return token;
            await Task.Delay(250);
        }
        return null;
    }

    private async Task AssertTenantStatusAsync(Guid tenantId, TenantStatus expected)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Id == tenantId);
        tenant.Status.Should().Be(expected);
    }

    private async Task WaitForSeedersAsync()
    {
        // Migrate explicitly first: the startup seeders swallow migration failures
        // (by design, so a dev app can boot without a DB), but the test must fail
        // loudly with the real error instead of timing out.
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<ONEVO.Domain.Features.Auth.Entities.Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
                    .AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady)
                    return;
            }
            catch
            {
                // Schema not created yet; keep polling.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    // ── HTTP helpers ────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string host,
        string path,
        object? body,
        string? cookie = null,
        string? csrfToken = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> GetJsonAsync(string host, string path, string? cookie = null)
    {
        var response = await SendAsync(HttpMethod.Get, host, path, body: null, cookie: cookie);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return json;
    }

    private async Task<(JsonElement Body, Dictionary<string, string> Cookies)> PostJsonAsync(
        string host, string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, host, path, body);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return (json, ParseSetCookies(response));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text)
            ? default
            : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return cookies;

        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var idx = pair.IndexOf('=');
            if (idx > 0)
                cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }

        return cookies;
    }
}
