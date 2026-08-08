using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.TrayActivation;

/// <summary>
/// Full-stack integration tests for the tray app activation flow:
/// generate (cookie-auth), exchange (anonymous), and refresh (anonymous) endpoints,
/// plus security invariants (one-time use, rate limiting, fingerprint theft detection,
/// raw code/token never persisted in plain text).
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TrayActivationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_tray_activation_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private TrayActivationTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new TrayActivationTestFactory(connectionString);
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // ── Baseline ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }

    // ── Generate endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_AuthenticatedUser_Returns200WithCodeAndExpiry()
    {
        var user = await SeedActiveUserAsync("gen-auth-test", "gen-auth@test.dev", "GenPass1!");
        var session = await LoginAndGetSessionAsync(user);

        var response = await PostGenerateAsync(session);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":");
        body.Should().Contain("\"expires_in_seconds\":600");
        using var doc = JsonDocument.Parse(body);
        var code = doc.RootElement.GetProperty("code").GetString()!;
        code.Should().HaveLength(8);
        code.Should().MatchRegex(@"^[A-Z2-9]+$", "code must use the defined safe character set");
    }

    [Fact]
    public async Task Generate_Unauthenticated_Returns401()
    {
        await SeedActiveUserAsync("gen-unauth-test", "gen-unauth@test.dev", "GenPass1!");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/generate");
        request.Headers.Host = "gen-unauth-test.localhost";
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Generate_ExceedsRateLimit_Returns429OnFourthRequest()
    {
        var user = await SeedActiveUserAsync("gen-rate-test", "gen-rate@test.dev", "RatePass1!");
        var session = await LoginAndGetSessionAsync(user);

        // First 3 requests must succeed
        for (var i = 0; i < 3; i++)
        {
            var ok = await PostGenerateAsync(session);
            ok.StatusCode.Should().Be(
                HttpStatusCode.OK,
                $"request {i + 1} of 3 should succeed");
        }

        // Fourth request in the same hour must be rejected
        var tooMany = await PostGenerateAsync(session);

        tooMany.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Exchange endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task Exchange_ValidCode_Returns200WithAccessAndRefreshTokens()
    {
        var user = await SeedActiveUserWithEmployeeAsync(
            "exchange-valid-test", "exchange-valid@test.dev", "ExchPass1!", "EMP-0001");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("access_token").GetString().Should().StartWith("eyJ");
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("expires_in_seconds").GetInt32().Should().Be(3600);
        doc.RootElement.GetProperty("refresh_expires_in_seconds").GetInt32().Should().Be(7_776_000);
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Priya Employee");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("priya.employee@test.dev");
        doc.RootElement.GetProperty("employee_number").GetString().Should().Be("EMP-0001");
        body.Should().NotContain(user.UserId.ToString(), "response must never expose internal user ID");
        body.Should().NotContain(user.TenantId.ToString(), "response must never expose internal tenant ID");
    }

    [Fact]
    public async Task Exchange_UserWithoutEmployeeRecord_FallsBackToUserNameAndEmail()
    {
        var user = await SeedActiveUserAsync(
            "exchange-noemp-test", "exchange-noemp@test.dev", "NoEmpPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var response = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-noemp");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Test User");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("exchange-noemp@test.dev");
        doc.RootElement.TryGetProperty("employee_number", out var numberProp).Should().BeTrue();
        numberProp.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Exchange_InvalidCode_Returns401()
    {
        var response = await PostExchangeAsync("BADCDE23", "My Laptop", "Windows 11", "fp-001");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exchange_CodeIsOneTimeUse_SecondCallReturns401()
    {
        var user = await SeedActiveUserAsync("exchange-otu-test", "exchange-otu@test.dev", "OtuPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        var first = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-otu");
        var second = await PostExchangeAsync(code, "My Laptop", "Windows 11", "fp-device-otu");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a consumed code must be rejected on replay");
    }

    [Fact]
    public async Task Exchange_RawCodeNeverStoredInDb_OnlyHashPersisted()
    {
        var user = await SeedActiveUserAsync("exchange-hash-test", "exchange-hash@test.dev", "HashPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedHashes = await db.TrayActivationCodes
            .Where(c => c.UserId == user.UserId)
            .Select(c => c.CodeHash)
            .ToListAsync();

        storedHashes.Should().NotBeEmpty();
        storedHashes.Should().NotContain(code, "only the SHA-256 hash of the code may ever be persisted");
    }

    // ── Refresh endpoint ───────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_RotatesRefreshToken_Returns200WithNewTokens()
    {
        var user = await SeedActiveUserAsync("refresh-valid-test", "refresh-valid@test.dev", "RefPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string fingerprint = "fp-refresh-valid-001";
        var (_, firstRefreshToken) = await ExchangeCodeAsync(code, fingerprint);

        var response = await PostRefreshAsync(firstRefreshToken, fingerprint);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("access_token").GetString().Should().StartWith("eyJ");
        var newRefreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;
        newRefreshToken.Should().NotBeNullOrEmpty();
        newRefreshToken.Should().NotBe(firstRefreshToken, "refresh token must be rotated on each use");
        doc.RootElement.GetProperty("employee_name").GetString().Should().Be("Test User");
        doc.RootElement.GetProperty("employee_email").GetString().Should().Be("refresh-valid@test.dev");
    }

    [Fact]
    public async Task Refresh_UsedToken_Returns401()
    {
        var user = await SeedActiveUserAsync("refresh-used-test", "refresh-used@test.dev", "RefUsedPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string fingerprint = "fp-refresh-used-001";
        var (_, firstRefreshToken) = await ExchangeCodeAsync(code, fingerprint);

        // Rotate once — old token is revoked
        await PostRefreshAsync(firstRefreshToken, fingerprint);

        // Replay the original (now revoked) token
        var replay = await PostRefreshAsync(firstRefreshToken, fingerprint);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a rotated-away token must be rejected");
    }

    [Fact]
    public async Task Refresh_FingerprintMismatch_RevokesAllDeviceTokens_Returns401()
    {
        var user = await SeedActiveUserAsync("refresh-fp-test", "refresh-fp@test.dev", "FpPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string originalFingerprint = "fp-original-device";
        var (_, refreshToken) = await ExchangeCodeAsync(code, originalFingerprint);

        // Attacker has the token but uses a different fingerprint
        var mismatch = await PostRefreshAsync(refreshToken, "fp-attacker-device");

        mismatch.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "fingerprint mismatch must be treated as token theft and rejected");

        // The legitimate device's token must also be revoked (theft response)
        var legitimate = await PostRefreshAsync(refreshToken, originalFingerprint);
        legitimate.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "after a fingerprint mismatch all device tokens must be revoked");
    }

    // ── Revoke endpoint ────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ValidToken_Returns204_AndDeactivatesDevice()
    {
        var user = await SeedActiveUserAsync("revoke-valid-test", "revoke-valid@test.dev", "RevPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string fingerprint = "fp-revoke-valid-001";
        var (accessToken, _) = await ExchangeCodeAsync(code, fingerprint);

        var response = await PostRevokeAsync(accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = await db.TrayDeviceRegistrations
            .SingleAsync(d => d.UserId == user.UserId && d.DeviceFingerprint == fingerprint);
        device.IsActive.Should().BeFalse("revoke must deactivate the device registration");
        device.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_ThenRefresh_Returns401()
    {
        var user = await SeedActiveUserAsync("revoke-refresh-test", "revoke-refresh@test.dev", "RevRefPass1!");
        var session = await LoginAndGetSessionAsync(user);
        var code = await GenerateCodeAsync(session);
        const string fingerprint = "fp-revoke-refresh-001";
        var (accessToken, refreshToken) = await ExchangeCodeAsync(code, fingerprint);

        var revoke = await PostRevokeAsync(accessToken);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await PostRefreshAsync(refreshToken, fingerprint);

        refresh.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "a revoked device's refresh token must no longer be usable");
    }

    [Fact]
    public async Task Revoke_NoToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/revoke");
        request.Headers.Host = "localhost";
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<SeedResult> SeedActiveUserAsync(string tenantSlug, string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = tenantSlug,
            Slug = tenantSlug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new SeedResult(tenant.Id, user.Id, email, password, tenantSlug);
    }

    private async Task<SeedResult> SeedActiveUserWithEmployeeAsync(
        string tenantSlug, string email, string password, string employeeNumber)
    {
        var seed = await SeedActiveUserAsync(tenantSlug, email, password);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = seed.TenantId,
            UserId = seed.UserId,
            EmployeeNumber = employeeNumber,
            FirstName = "Priya",
            LastName = "Employee",
            Email = "priya.employee@test.dev",
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = seed.UserId
        });
        await db.SaveChangesAsync();

        return seed;
    }

    /// <summary>
    /// Performs the full base-domain login → legal acceptance → tenant session exchange flow
    /// and returns the session cookie string, CSRF header value, and tenant host for use in
    /// subsequent TenantPolicy-protected requests.
    /// </summary>
    private async Task<SessionInfo> LoginAndGetSessionAsync(SeedResult user)
    {
        // 1. Password login on base domain
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "localhost";
        loginRequest.Content = JsonContent.Create(new { email = user.Email, password = user.Password });
        var loginResponse = await _client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            await loginResponse.Content.ReadAsStringAsync());

        // 2. Complete legal acceptance (required for brand-new users without acceptance records)
        var legalResponse = await CompleteLegalAcceptanceAsync(loginResponse);
        legalResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            await legalResponse.Content.ReadAsStringAsync());

        // 3. Session exchange on tenant host — produces onevo_session + onevo_csrf cookies
        var exchangeResponse = await CompleteTenantSessionExchangeAsync(legalResponse);
        exchangeResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await exchangeResponse.Content.ReadAsStringAsync());

        var sessionValue = ExtractCookieValue(exchangeResponse, "onevo_session");
        var csrfCookieValue = ExtractCookieValue(exchangeResponse, "onevo_csrf");
        var csrfHeader = Uri.UnescapeDataString(csrfCookieValue);

        return new SessionInfo(
            $"onevo_session={sessionValue}; onevo_csrf={csrfCookieValue}",
            csrfHeader,
            $"{user.TenantSlug}.localhost");
    }

    private async Task<string> GenerateCodeAsync(SessionInfo session)
    {
        var response = await PostGenerateAsync(session);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<(string AccessToken, string RefreshToken)> ExchangeCodeAsync(
        string code, string fingerprint)
    {
        var response = await PostExchangeAsync(code, "Test Device", "Windows 11", fingerprint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return (
            doc.RootElement.GetProperty("access_token").GetString()!,
            doc.RootElement.GetProperty("refresh_token").GetString()!);
    }

    private async Task<HttpResponseMessage> PostGenerateAsync(SessionInfo session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/generate");
        request.Headers.Host = session.TenantHost;
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostExchangeAsync(
        string code, string deviceName, string deviceOs, string deviceFingerprint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/exchange");
        request.Headers.Host = "localhost";
        // Server deserializes ExchangeRequest(Code, DeviceName, DeviceOs, DeviceFingerprint)
        // with the default camelCase naming policy, so use camelCase property names here.
        request.Content = JsonContent.Create(new
        {
            code,
            deviceName,
            deviceOs,
            deviceFingerprint
        });
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostRefreshAsync(string refreshToken, string deviceFingerprint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/refresh");
        request.Headers.Host = "localhost";
        // Server deserializes RefreshRequest(RefreshToken, DeviceFingerprint) with camelCase policy.
        request.Content = JsonContent.Create(new
        {
            refreshToken,
            deviceFingerprint
        });
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostRevokeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/revoke");
        request.Headers.Host = "localhost";
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage priorResponse)
    {
        var legalPending = ExtractCookieValue(priorResponse, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(priorResponse, "onevo_legal_csrf");
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(
            priorDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
        request.Headers.Add("X-CSRF-Token", legalCsrf);
        request.Content = JsonContent.Create(new
        {
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CompleteTenantSessionExchangeAsync(HttpResponseMessage priorResponse)
    {
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(
            priorDocument.RootElement.GetProperty("continue_url").GetString()!,
            UriKind.Absolute);
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session-exchange");
        request.Headers.Host = continueUrl.Host;
        request.Content = JsonContent.Create(new { code });
        return await _client.SendAsync(request);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Enumerable.Empty<string>();

        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';')[0];
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }

        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    // ── Value objects ──────────────────────────────────────────────────────────

    private sealed record SeedResult(Guid TenantId, Guid UserId, string Email, string Password, string TenantSlug);

    private sealed record SessionInfo(string CookieHeader, string CsrfHeader, string TenantHost);
}
