using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.CheckIn;

/// <summary>
/// Full-stack integration tests for tray employee check-in:
/// submit check-in + face-scan upload under TrayDeviceScheme JWT auth.
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class CheckInIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_checkin_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private CheckInTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new CheckInTestFactory(connectionString);
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

    // ── Migrations ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        pending.Should().BeEmpty();
    }

    // ── SubmitCheckIn ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitCheckIn_WithValidTrayJwt_Returns200AndPersistsRecord()
    {
        var jwt = await GetTrayJwtAsync("checkin-ok");
        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 6.9271,
            longitude = 79.8612,
            location_accuracy = 15.0,
            location_address = "Colombo, Sri Lanka",
            device_serial_number = "SN-TEST-001"
        }, jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("check_in_id").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("latitude").GetDouble().Should().BeApproximately(6.9271, 0.0001);
        body.GetProperty("device_serial_number").GetString().Should().Be("SN-TEST-001");
        body.GetProperty("face_scan_required").GetBoolean().Should().BeTrue();

        var checkInId = Guid.Parse(body.GetProperty("check_in_id").GetString()!);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await db.EmployeeCheckIns.FindAsync(checkInId);
        record.Should().NotBeNull();
        record!.Latitude.Should().BeApproximately(6.9271, 0.0001);
        record.DeviceSerialNumber.Should().Be("SN-TEST-001");
    }

    [Fact]
    public async Task SubmitCheckIn_WithoutJwt_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/check-in");
        req.Headers.Host = "localhost";
        req.Content = JsonContent.Create(new { latitude = 6.9271, longitude = 79.8612 });

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SubmitCheckIn_WithInvalidLatitude_Returns400()
    {
        var jwt = await GetTrayJwtAsync("checkin-lat");
        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 999.0,
            longitude = 79.8612
        }, jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitCheckIn_WithNoLocationOrDevice_Returns200()
    {
        var jwt = await GetTrayJwtAsync("checkin-min");
        using var req = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { }, jwt);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    // ── UploadFaceScan ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFaceScan_AfterCheckIn_Returns200AndPersistsMetadata()
    {
        var jwt = await GetTrayJwtAsync("checkin-face");

        using var checkInReq = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new
        {
            latitude = 6.9271,
            longitude = 79.8612
        }, jwt);
        var checkInResp = await _client.SendAsync(checkInReq);
        checkInResp.StatusCode.Should().Be(HttpStatusCode.OK, await checkInResp.Content.ReadAsStringAsync());
        var checkInId = (await checkInResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("check_in_id").GetString()!;

        var fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xD9 };
        using var form = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(fakeJpeg);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(imageContent, "face_scan", "scan.jpg");

        using var scanReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Host = "localhost";
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var scanResp = await _client.SendAsync(scanReq);

        scanResp.StatusCode.Should().Be(HttpStatusCode.OK, await scanResp.Content.ReadAsStringAsync());
        var scanBody = await scanResp.Content.ReadFromJsonAsync<JsonElement>();
        scanBody.GetProperty("face_scan_id").GetString().Should().NotBeNullOrEmpty();
        scanBody.GetProperty("status").GetString().Should().Be("available");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var faceScan = await db.MonitoringFaceScans
            .FirstOrDefaultAsync(f => f.CheckInId == Guid.Parse(checkInId));
        faceScan.Should().NotBeNull();
        faceScan!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task UploadFaceScan_WithWrongContentType_Returns400()
    {
        var jwt = await GetTrayJwtAsync("checkin-ctype");

        using var checkInReq = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { }, jwt);
        var checkInResp = await _client.SendAsync(checkInReq);
        checkInResp.StatusCode.Should().Be(HttpStatusCode.OK, await checkInResp.Content.ReadAsStringAsync());
        var checkInId = (await checkInResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("check_in_id").GetString()!;

        using var form = new MultipartFormDataContent();
        var pdfContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(pdfContent, "face_scan", "scan.pdf");

        using var scanReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Host = "localhost";
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await _client.SendAsync(scanReq);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadFaceScan_ForAnotherUsersCheckIn_Returns403()
    {
        // Same tenant, two different employees — check-in is visible under RLS,
        // but ownership mismatch must return 403 (not 404).
        var slug = $"checkin-u-{Guid.NewGuid():N}"[..20];
        var password = "TestPass1!";
        var user1 = await SeedActiveUserAsync(slug, $"{slug}-a@test.dev", password);
        var user2 = await SeedSecondUserInTenantAsync(user1.TenantId, $"{slug}-b@test.dev", password);

        var jwt1 = await GetTrayJwtForUserAsync(user1, fingerprint: $"fp-{slug}-a");
        var jwt2 = await GetTrayJwtForUserAsync(user2, fingerprint: $"fp-{slug}-b");

        using var checkInReq = TrayJsonRequest(HttpMethod.Post, "/api/v1/monitoring/check-in", new { }, jwt1);
        var checkInResp = await _client.SendAsync(checkInReq);
        checkInResp.StatusCode.Should().Be(HttpStatusCode.OK, await checkInResp.Content.ReadAsStringAsync());
        var checkInId = (await checkInResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("check_in_id").GetString()!;

        var fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xD9 };
        using var form = new MultipartFormDataContent();
        var img = new ByteArrayContent(fakeJpeg);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "face_scan", "scan.jpg");

        using var scanReq = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/monitoring/check-in/{checkInId}/face-scan")
        {
            Content = form
        };
        scanReq.Headers.Host = "localhost";
        scanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt2);

        var resp = await _client.SendAsync(scanReq);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> GetTrayJwtAsync(string slugPrefix)
    {
        // Keep slug within DB length limits and unique across parallel tests.
        var slug = $"{slugPrefix}-{Guid.NewGuid():N}"[..20];
        var email = $"{slug}@test.dev";
        var password = "TestPass1!";
        var user = await SeedActiveUserAsync(slug, email, password);
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

    private async Task<SeedResult> SeedSecondUserInTenantAsync(Guid tenantId, string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        var legalEntity = await db.LegalEntities.SingleAsync(le => le.TenantId == tenantId);
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            FirstName = "Second",
            LastName = "User",
            IsActive = true
        };

        db.Users.Add(user);
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Second",
            LastName = "User",
            Email = email,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = user.Id
        });
        await db.SaveChangesAsync();

        return new SeedResult(tenantId, user.Id, email, password, tenant.Slug);
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

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = $"{tenantSlug} Company",
            CountryCode = "US",
            CurrencyCode = "USD",
            IsActive = true,
            IsPrimary = true
        };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "User",
            Email = email,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = user.Id
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        db.LegalEntities.Add(legalEntity);
        db.Employees.Add(employee);
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
