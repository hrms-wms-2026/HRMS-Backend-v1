using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.ActivityMonitoring;

/// <summary>
/// Full-stack integration tests for activity snapshot ingest under TrayDeviceScheme.
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ActivityIngestIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_activity_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private ActivityMonitoringTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new ActivityMonitoringTestFactory(connectionString);
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
    public async Task Ingest_WithValidTrayJwt_AndToggleOn_Returns202_AndPersists()
    {
        var slug = $"act-ok-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await EnableActivityMonitoringAsync(user.TenantId);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/activity/snapshots", new
        {
            snapshots = new[]
            {
                new
                {
                    captured_at = capturedAt,
                    keyboard_events_count = 42,
                    mouse_events_count = 17,
                    active_seconds = 60,
                    idle_seconds = 0,
                    intensity_score = 55.5m,
                    foreground_process_name = "code.exe"
                }
            }
        }, jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, await resp.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var snapshots = await db.ActivitySnapshots
            .Where(s => s.TenantId == user.TenantId && s.EmployeeId == user.UserId)
            .ToListAsync();
        snapshots.Should().HaveCount(1);
        snapshots[0].KeyboardEventsCount.Should().Be(42);
        snapshots[0].MouseEventsCount.Should().Be(17);
        snapshots[0].ForegroundProcessName.Should().Be("code.exe");

        var buffers = await db.ActivityRawBuffers
            .Where(b => b.TenantId == user.TenantId)
            .ToListAsync();
        buffers.Should().HaveCount(1);
        buffers[0].PayloadJson.Should().Contain("keyboard");
    }

    [Fact]
    public async Task Ingest_WhenActivityMonitoringDisabled_Returns403()
    {
        var slug = $"act-off-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        // No toggle row → safe default false
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/activity/snapshots", new
        {
            snapshots = new[]
            {
                new
                {
                    captured_at = DateTimeOffset.UtcNow.AddMinutes(-1),
                    keyboard_events_count = 1,
                    mouse_events_count = 1,
                    active_seconds = 10,
                    idle_seconds = 0,
                    intensity_score = 10m,
                    foreground_process_name = (string?)null
                }
            }
        }, jwt);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ingest_WithoutJwt_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activity/snapshots");
        req.Headers.Host = "localhost";
        req.Content = JsonContent.Create(new { snapshots = Array.Empty<object>() });

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ingest_EmptyBatch_Returns400()
    {
        var slug = $"act-empty-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await EnableActivityMonitoringAsync(user.TenantId);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/activity/snapshots", new
        {
            snapshots = Array.Empty<object>()
        }, jwt);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task EnableActivityMonitoringAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MonitoringFeatureToggles.Add(new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActivityMonitoring = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
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

    private static HttpRequestMessage TrayJsonRequest(HttpMethod method, string path, object body, string jwt)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = JsonContent.Create(body);
        return req;
    }

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
