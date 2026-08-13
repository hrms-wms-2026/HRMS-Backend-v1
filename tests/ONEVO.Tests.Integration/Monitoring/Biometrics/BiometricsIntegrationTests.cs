using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.Biometrics;

/// <summary>
/// Full-stack integration tests for biometric enrollment: create/complete an enrollment
/// attempt and read the resulting profile, under TrayDeviceScheme JWT auth. Requires Docker.
/// NOT run in the session that wrote this file — no Docker/Postgres available in that sandbox.
/// This file is a structural mirror of CheckInIntegrationTests.cs/CheckInTestFactory.cs, adapted
/// to seed a real CoreHR Employee row (required for IEmployeeIdentityResolver to succeed, which
/// CheckIn's tests never needed). The Employee row's EmploymentTypeId/EmploymentStatusId/
/// WorkModeId defaults (all 1) assume a seeded lookup row with id=1 exists for each — verify
/// this against a real dev DB before trusting these tests as passing.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BiometricsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_biometrics_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private BiometricsTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new BiometricsTestFactory(connectionString);
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

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEnrollmentAttempt_WithValidTrayJwt_Returns200AndAwsSession()
    {
        var jwt = await GetTrayJwtAsync("bio-create");

        var response = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("aws_session_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateEnrollmentAttempt_WithoutEmployeeProfile_Returns422()
    {
        var jwt = await GetTrayJwtAsync("bio-noemp", seedEmployee: false);

        var response = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateEnrollmentAttempt_WithoutJwt_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/biometrics/enrollment-attempts");
        req.Headers.Host = "localhost";

        var response = await _client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteEnrollmentAttempt_AfterCreate_Returns200AndActiveProfile()
    {
        var jwt = await GetTrayJwtAsync("bio-complete");
        var createResp = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, await createResp.Content.ReadAsStringAsync());
        var attemptId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attempt_id").GetGuid();

        var completeResp = await PostAsync(
            $"/api/v1/monitoring/biometrics/enrollment-attempts/{attemptId}/complete", jwt);

        completeResp.StatusCode.Should().Be(HttpStatusCode.OK, await completeResp.Content.ReadAsStringAsync());
        var profile = await completeResp.Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("status").GetString().Should().Be("active");
    }

    [Fact]
    public async Task GetProfile_AfterEnrollment_ReturnsActiveProfile()
    {
        var jwt = await GetTrayJwtAsync("bio-getprofile");
        var createResp = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);
        var attemptId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attempt_id").GetGuid();
        await PostAsync($"/api/v1/monitoring/biometrics/enrollment-attempts/{attemptId}/complete", jwt);

        var response = await GetAsync("/api/v1/monitoring/biometrics/profile", jwt);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetProfile_WithoutEnrollment_Returns404()
    {
        var jwt = await GetTrayJwtAsync("bio-noprofile");

        var response = await GetAsync("/api/v1/monitoring/biometrics/profile", jwt);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReEnrollment_SupersedesPreviousProfile()
    {
        var jwt = await GetTrayJwtAsync("bio-reenroll");

        var first = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);
        var firstAttemptId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attempt_id").GetGuid();
        await PostAsync($"/api/v1/monitoring/biometrics/enrollment-attempts/{firstAttemptId}/complete", jwt);

        var second = await PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", jwt);
        var secondAttemptId = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attempt_id").GetGuid();
        var completeSecond = await PostAsync(
            $"/api/v1/monitoring/biometrics/enrollment-attempts/{secondAttemptId}/complete", jwt);

        completeSecond.StatusCode.Should().Be(HttpStatusCode.OK, await completeSecond.Content.ReadAsStringAsync());

        // Exactly one Active row is enforced by the partial unique index — a second Active row
        // here would have thrown a Postgres unique-violation on SaveChangesAsync.
        var profileResp = await GetAsync("/api/v1/monitoring/biometrics/profile", jwt);
        var profile = await profileResp.Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("status").GetString().Should().Be("active");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostAsync(string path, string jwt)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string jwt)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return _client.SendAsync(req);
    }

    private async Task<string> GetTrayJwtAsync(string slugPrefix, bool seedEmployee = true)
    {
        var slug = $"{slugPrefix}-{Guid.NewGuid():N}"[..20];
        var email = $"{slug}@test.dev";
        var password = "TestPass1!";
        var user = await SeedActiveUserAsync(slug, email, password, seedEmployee);
        return await GetTrayJwtForUserAsync(user, fingerprint: $"fp-{slug}");
    }

    private async Task<string> GetTrayJwtForUserAsync(SeedResult user, string fingerprint)
    {
        var session = await LoginAndGetSessionAsync(user);

        var genResp = await PostGenerateAsync(session);
        genResp.StatusCode.Should().Be(HttpStatusCode.OK, await genResp.Content.ReadAsStringAsync());
        var code = (await genResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

        using var exchReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/exchange");
        exchReq.Headers.Host = "localhost";
        exchReq.Content = JsonContent.Create(new
        {
            code,
            deviceName = "Test Device",
            deviceOs = "Windows",
            deviceFingerprint = fingerprint
        });
        var exchResp = await _client.SendAsync(exchReq);
        exchResp.StatusCode.Should().Be(HttpStatusCode.OK, await exchResp.Content.ReadAsStringAsync());
        return (await exchResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    private async Task<SeedResult> SeedActiveUserAsync(string tenantSlug, string email, string password, bool seedEmployee)
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

        if (seedEmployee)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                EmployeeNumber = $"EMP-{tenantSlug}",
                FirstName = "Test",
                LastName = "User",
                Email = email,
                HireDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedById = user.Id
            });
        }

        await db.SaveChangesAsync();

        return new SeedResult(tenant.Id, user.Id, email, password, tenantSlug);
    }

    private async Task<SessionInfo> LoginAndGetSessionAsync(SeedResult user)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "localhost";
        loginRequest.Content = JsonContent.Create(new { email = user.Email, password = user.Password });
        var loginResponse = await _client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            await loginResponse.Content.ReadAsStringAsync());

        var legalResponse = await CompleteLegalAcceptanceAsync(loginResponse);
        legalResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            await legalResponse.Content.ReadAsStringAsync());

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

    private async Task<HttpResponseMessage> PostGenerateAsync(SessionInfo session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/generate");
        request.Headers.Host = session.TenantHost;
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
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

    private sealed record SeedResult(Guid TenantId, Guid UserId, string Email, string Password, string TenantSlug);

    private sealed record SessionInfo(string CookieHeader, string CsrfHeader, string TenantHost);
}
