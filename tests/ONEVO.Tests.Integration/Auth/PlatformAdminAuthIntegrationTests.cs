using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// End-to-end proof of the canonical Developer Platform admin auth flow:
/// database-backed platform user, cookie session in platform_user_sessions,
/// session-bound CSRF, database-resolved permissions on /admin/v1/tenants,
/// and platform_auth_events on success/failure/logout.
/// </summary>
public class PlatformAdminAuthIntegrationTests : IAsyncLifetime
{
    private const string AdminEmail = "test_admin@onevo.dev";
    private const string AdminPassword = "test_password_123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_platform_auth_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await AdminTestFactory.MigrateDatabaseAsync(_postgres.GetConnectionString());
        _factory = new AdminTestFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    private ApplicationDbContext GetDb(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private async Task<(string SessionCookie, string CsrfToken)> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var session = cookies.First(c => c.StartsWith("admin_session=")).Split(';')[0];
        var csrf = cookies.First(c => c.StartsWith("admin_csrf=")).Split(';')[0].Split('=', 2)[1];
        return (session, csrf);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string sessionCookie, string? csrf = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", sessionCookie);
        if (csrf is not null)
            request.Headers.Add("X-CSRF-Token", csrf);
        return request;
    }

    [Fact]
    public async Task Login_Succeeds_CreatesPlatformUserSession_NoTokensInBody_WritesAuthEvent()
    {
        var response = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login", new { email = AdminEmail, password = AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cookie session, not tokens: response body must not contain JWT/token fields.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainEquivalentOf("accessToken");
        body.Should().NotContainEquivalentOf("access_token");
        body.Should().NotContainEquivalentOf("refresh_token");
        body.Should().NotContainEquivalentOf("refreshToken");
        body.Should().NotContainEquivalentOf("bearer");
        body.Should().NotContainEquivalentOf("jwt");

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var sessionCookie = cookies.First(c => c.StartsWith("admin_session=")).Split(';')[0];
        var rawCookieValue = sessionCookie.Split('=', 2)[1];

        using var scope = _factory.Services.CreateScope();
        var db = GetDb(scope);

        var session = await db.PlatformUserSessions.SingleAsync();
        session.RevokedAt.Should().BeNull();
        session.TokenHash.Should().MatchRegex("^[0-9a-f]{64}$");
        // The stored value must be a hash, never the browser cookie payload.
        rawCookieValue.Should().NotContain(session.TokenHash);
        session.CsrfTokenHash.Should().NotBeNullOrEmpty();

        var user = await db.PlatformUsers.SingleAsync(u => u.Email == AdminEmail);
        session.AccountId.Should().Be(user.Id);
        user.LastLoginAt.Should().NotBeNull();

        var successEvent = await db.PlatformAuthEvents
            .SingleAsync(e => e.EventType == "login_succeeded");
        successEvent.UserId.Should().Be(user.Id);
        (successEvent.MetadataJson ?? "").Should().NotContain(AdminPassword);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401_AndWritesFailureEventWithoutSecrets()
    {
        var response = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login", new { email = AdminEmail, password = "wrong_password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = GetDb(scope);
        var failureEvent = await db.PlatformAuthEvents
            .SingleAsync(e => e.EventType == "login_failed");
        (failureEvent.MetadataJson ?? "").Should().NotContain("wrong_password");
        (failureEvent.MetadataJson ?? "").Should().NotContain(AdminPassword);
    }

    [Fact]
    public async Task Login_InactivePlatformUser_IsRejected()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDb(scope);
            var user = await db.PlatformUsers.SingleAsync(u => u.Email == AdminEmail);
            user.Status = PlatformUser.StatusInactive;
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/admin/v1/auth/login", new { email = AdminEmail, password = AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantsEndpoint_RequiresDocumentedPermission_403WithoutRole_200WithRole()
    {
        var (sessionCookie, _) = await LoginAsync();

        // With the Super Admin role (all permissions) the endpoint succeeds.
        var allowed = await _client.SendAsync(
            BuildRequest(HttpMethod.Get, "/admin/v1/tenants", sessionCookie));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        // Remove the user's role: permissions are resolved from the database on
        // every request, so the same session must now be denied with the 403 contract.
        Guid roleId, userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDb(scope);
            var assignment = await db.PlatformUserRoles.SingleAsync();
            (userId, roleId) = (assignment.UserId, assignment.RoleId);
            db.PlatformUserRoles.Remove(assignment);
            await db.SaveChangesAsync();
        }

        var denied = await _client.SendAsync(
            BuildRequest(HttpMethod.Get, "/admin/v1/tenants", sessionCookie));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        deniedBody.Should().Contain("permission_denied");
        deniedBody.Should().Contain(PlatformPermissionCatalog.TenantsRead);

        // Restore the role: access returns without a new login.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDb(scope);
            db.PlatformUserRoles.Add(new PlatformUserRole { UserId = userId, RoleId = roleId });
            await db.SaveChangesAsync();
        }

        var restored = await _client.SendAsync(
            BuildRequest(HttpMethod.Get, "/admin/v1/tenants", sessionCookie));
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MutatingAdminRequest_WithoutCsrfToken_IsRejected()
    {
        var (sessionCookie, csrf) = await LoginAsync();

        var withoutCsrf = await _client.SendAsync(BuildRequest(
            HttpMethod.Post, "/admin/v1/tenants", sessionCookie, csrf: null));
        withoutCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var wrongCsrf = await _client.SendAsync(BuildRequest(
            HttpMethod.Post, "/admin/v1/tenants", sessionCookie, csrf: "forged-token"));
        wrongCsrf.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_RevokesSessionInPlatformUserSessions_AndWritesAuthEvent()
    {
        var (sessionCookie, csrf) = await LoginAsync();

        var logout = await _client.SendAsync(BuildRequest(
            HttpMethod.Post, "/admin/v1/auth/logout", sessionCookie, csrf));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDb(scope);
            var session = await db.PlatformUserSessions.SingleAsync();
            session.RevokedAt.Should().NotBeNull();

            var revokedEvent = await db.PlatformAuthEvents
                .SingleAsync(e => e.EventType == "session_revoked");
            revokedEvent.UserId.Should().Be(session.AccountId);
        }

        // The revoked session must no longer authenticate.
        var afterLogout = await _client.SendAsync(
            BuildRequest(HttpMethod.Get, "/admin/v1/tenants", sessionCookie));
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnauthenticatedRequest_401Body_IncludesCorrelationId()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/tenants");
        request.Headers.Add("X-Correlation-Id", "test-correlation-401");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("correlationId");
        body.Should().Contain("test-correlation-401");
    }

    [Fact]
    public async Task ForbiddenRequest_403Body_IncludesCorrelationId()
    {
        var (sessionCookie, _) = await LoginAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDb(scope);
            var assignment = await db.PlatformUserRoles.SingleAsync();
            db.PlatformUserRoles.Remove(assignment);
            await db.SaveChangesAsync();
        }

        var request = BuildRequest(HttpMethod.Get, "/admin/v1/tenants", sessionCookie);
        request.Headers.Add("X-Correlation-Id", "test-correlation-403");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("correlationId");
        body.Should().Contain("test-correlation-403");
    }
}
