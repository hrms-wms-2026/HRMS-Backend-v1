using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Settings;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MonitoringFeatureTogglesIntegrationTests : IAsyncLifetime
{
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_monitoring_settings_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, new CapturingEmailService());
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
    public async Task Get_Unauthenticated_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/settings");
        req.Headers.Host = "localhost";

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_NoRowYet_ReturnsAllFalseDefaults()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-get");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/monitoring/settings");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("activityMonitoring").GetBoolean().Should().BeFalse();
        body.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_MissingConfigurePermission_Returns403()
    {
        var session = await SeedUserWithPermissionsAsync("mft-noperm", ["monitoring:read"]);

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        req.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        req.Content = JsonContent.Create(ToggleBody(activityMonitoring: true));

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The actual product claim this feature exists to satisfy: after PUT, the
    /// resolver that every ingest endpoint calls (MonitoringToggleResolverService)
    /// sees the new value - not just that the row changed in the database.
    /// </summary>
    [Fact]
    public async Task Put_ActivityMonitoringTrue_ResolverReflectsChange()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-resolver");
        // Resolves via the admin's own Employee row: no employee-level override exists, so this
        // falls through to the legal-entity default the PUT below just wrote.
        var employeeId = session.UserId;

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        putReq.Headers.Host = session.TenantHost;
        putReq.Headers.Add("Cookie", session.CookieHeader);
        putReq.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        putReq.Content = JsonContent.Create(ToggleBody(activityMonitoring: true));

        var putResp = await _client.SendAsync(putReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK, await putResp.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(
            session.TenantId, employeeId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Put_IdleThresholdMinutes_ResolverReflectsChange()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-idle-threshold");
        var employeeId = session.UserId;

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        putReq.Headers.Host = session.TenantHost;
        putReq.Headers.Add("Cookie", session.CookieHeader);
        putReq.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        putReq.Content = JsonContent.Create(ToggleBody(activityMonitoring: true, idleThresholdMinutes: 20));

        var putResp = await _client.SendAsync(putReq);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK, await putResp.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var minutes = await resolver.GetIdleThresholdMinutesAsync(session.TenantId, employeeId);

        minutes.Should().Be(20);
    }

    [Fact]
    public async Task Put_IdleThresholdMinutesOutOfRange_Returns400()
    {
        var session = await SeedAdminUserAndLoginAsync("mft-idle-threshold-invalid");

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/monitoring/settings");
        putReq.Headers.Host = session.TenantHost;
        putReq.Headers.Add("Cookie", session.CookieHeader);
        putReq.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        putReq.Content = JsonContent.Create(ToggleBody(activityMonitoring: true, idleThresholdMinutes: 120));

        var putResp = await _client.SendAsync(putReq);

        putResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static object ToggleBody(bool activityMonitoring, int idleThresholdMinutes = 5) => new
    {
        activityMonitoring,
        applicationTracking = false,
        documentTracking = false,
        communicationTracking = false,
        screenshotCapture = false,
        autoScreenshotCapture = false,
        meetingDetection = false,
        deviceTracking = false,
        workLocationVerification = false,
        identityVerification = false,
        biometric = false,
        idleThresholdMinutes
    };

    private Task<SessionInfo> SeedAdminUserAndLoginAsync(string slug) =>
        SeedUserWithPermissionsAsync(slug, ["monitoring:read", "monitoring:configure"]);

    private async Task<SessionInfo> SeedUserWithPermissionsAsync(string slug, IReadOnlyList<string> permissionCodes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            TenantId = tenant.Id,
            Email = $"{slug}@test.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass1!", 12),
            FirstName = "Test",
            LastName = "Admin",
            IsActive = true
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);

        var now = DateTimeOffset.UtcNow;
        db.Add(new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = SeededPlanId,
            Status = "active",
            BillingCycle = "monthly",
            CommercialModel = "subscription",
            BillingCurrency = "USD",
            CompanySizeRange = "1-10",
            SelectedModulesJson = """["monitoring"]""",
            CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
            CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAt = now
        });

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenant.Id,
            Name = $"{slug}-role",
            Description = "Monitoring settings fixture role",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });
        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenant.Id, RoleId = roleId, PermissionId = permission.Id });
        }
        db.Add(new UserRole
        {
            TenantId = tenant.Id, UserId = userId, RoleId = roleId,
            AssignedAt = now, AssignedBy = userId
        });

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = $"{slug} Company",
            CountryCode = "US",
            CurrencyCode = "USD",
            IsActive = true,
            IsPrimary = true
        };
        db.LegalEntities.Add(legalEntity);
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            LegalEntityId = legalEntity.Id,
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Admin",
            Email = $"{slug}@test.dev",
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = now,
            CreatedById = userId
        });

        await db.SaveChangesAsync();

        var sessionInfo = await LoginAndGetSessionAsync(userId, $"{slug}@test.dev", "TestPass1!", slug);
        return sessionInfo with { TenantId = tenant.Id, UserId = userId };
    }

    private async Task<SessionInfo> LoginAndGetSessionAsync(Guid userId, string email, string password, string tenantSlug)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "localhost";
        loginRequest.Content = JsonContent.Create(new { email, password });
        var loginResponse = await _client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await loginResponse.Content.ReadAsStringAsync());

        var legalResponse = await CompleteLegalAcceptanceAsync(loginResponse);
        legalResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await legalResponse.Content.ReadAsStringAsync());

        var exchangeResponse = await CompleteTenantSessionExchangeAsync(legalResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exchangeResponse.Content.ReadAsStringAsync());

        var sessionValue = ExtractCookieValue(exchangeResponse, "onevo_session");
        var csrfCookieValue = ExtractCookieValue(exchangeResponse, "onevo_csrf");
        var csrfHeader = Uri.UnescapeDataString(csrfCookieValue);

        return new SessionInfo(
            $"onevo_session={sessionValue}; onevo_csrf={csrfCookieValue}",
            csrfHeader,
            $"{tenantSlug}.localhost",
            Guid.Empty,
            Guid.Empty);
    }

    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage priorResponse)
    {
        var legalPending = ExtractCookieValue(priorResponse, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(priorResponse, "onevo_legal_csrf");
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);

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
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session-exchange");
        request.Headers.Host = continueUrl.Host;
        request.Content = JsonContent.Create(new { code });
        return await _client.SendAsync(request);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values : Enumerable.Empty<string>();
        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';')[0];
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }
        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    private sealed record SessionInfo(
        string CookieHeader, string CsrfHeader, string TenantHost, Guid TenantId, Guid UserId);
}
