using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.Policy;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TrayMonitoringPolicyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_policy_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private TrayMonitoringPolicyTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new TrayMonitoringPolicyTestFactory(connectionString);
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
    public async Task GetPolicy_WithoutJwt_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/tray/policy");
        req.Headers.Host = "localhost";

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPolicy_WithScreenshotTogglesOn_ReturnsInactivityEnabled()
    {
        var slug = $"pol-on-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await SeedTogglesAsync(user.TenantId, activity: true, screenshot: true, autoScreenshot: true);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/tray/policy");
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("activity_signal_enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("screenshot_enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("inactivity_screenshot_enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();

        var validUntil = body.GetProperty("valid_until").GetDateTimeOffset();
        validUntil.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
        validUntil.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(70));
    }

    [Fact]
    public async Task GetPolicy_AutoScreenshotOff_ReturnsInactivityDisabled()
    {
        var slug = $"pol-off-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await SeedTogglesAsync(user.TenantId, activity: true, screenshot: true, autoScreenshot: false);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/tray/policy");
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("screenshot_enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("inactivity_screenshot_enabled").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// The tray endpoint serves TrayAgentPolicyDto, which carries the newly-added
    /// LocationTrackingEnabled/EffectiveScope fields. Proves both round-trip through the
    /// full resolver + JSON contract, not just that the DTO record has the properties.
    /// </summary>
    [Fact]
    public async Task GetPolicy_WithWorkLocationVerificationOn_ReturnsLocationAndScopeFields()
    {
        var slug = $"pol-loc-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await SeedTogglesAsync(user.TenantId, activity: true, screenshot: false, autoScreenshot: false, workLocationVerification: true);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/tray/policy");
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("location_tracking_enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("effective_scope").GetString().Should().Be("employee");
    }

    private Task SeedTogglesAsync(Guid tenantId, bool activity, bool screenshot, bool autoScreenshot) =>
        SeedTogglesAsync(tenantId, activity, screenshot, autoScreenshot, workLocationVerification: false);

    private async Task SeedTogglesAsync(
        Guid tenantId, bool activity, bool screenshot, bool autoScreenshot, bool workLocationVerification)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legalEntityId = await db.LegalEntities
            .Where(entity => entity.TenantId == tenantId && entity.IsPrimary)
            .Select(entity => entity.Id)
            .SingleAsync();
        db.MonitoringFeatureToggles.Add(new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            ActivityMonitoring = activity,
            ApplicationTracking = true,
            ScreenshotCapture = screenshot,
            AutoScreenshotCapture = autoScreenshot,
            WorkLocationVerification = workLocationVerification,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> GetTrayJwtForUserAsync(SeedResult user, string fingerprint)
    {
        var session = await LoginAndGetSessionAsync(user);

        using var genReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/activation/generate");
        genReq.Headers.Host = session.TenantHost;
        genReq.Headers.Add("Cookie", session.CookieHeader);
        genReq.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        var genResp = await _client.SendAsync(genReq);
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
        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = $"{tenantSlug} Company",
            CountryCode = "US", CurrencyCode = "USD", IsActive = true, IsPrimary = true
        };
        db.LegalEntities.Add(legalEntity);
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = user.Id,
            LegalEntityId = legalEntity.Id, EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test", LastName = "User", Email = email,
            EmploymentTypeId = 1, EmploymentStatusId = 1, WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1), CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = user.Id
        });
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
