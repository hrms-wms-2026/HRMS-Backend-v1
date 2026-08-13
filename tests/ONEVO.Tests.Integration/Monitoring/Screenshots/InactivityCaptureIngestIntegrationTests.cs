using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Monitoring.Screenshots;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Monitoring.Screenshots;

/// <summary>
/// Full-stack integration tests for inactivity capture ingest under TrayDeviceScheme.
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class InactivityCaptureIngestIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_inactivity_integration_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private InactivityCaptureIngestTestFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly DateTimeOffset IdleStart = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
    private static readonly DateTimeOffset PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z");
    private static readonly DateTimeOffset DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z");
    private static readonly DateTimeOffset CapturedAt = DateTimeOffset.Parse("2026-08-10T01:05:05Z");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new InactivityCaptureIngestTestFactory(connectionString);
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
    public async Task Submit_CapturedJpeg_Returns200_AndPersistsEvidence()
    {
        var slug = $"cap-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await EnableCaptureTogglesAsync(user.TenantId);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");
        var attemptId = Guid.NewGuid();

        using var req = BuildMultipartRequest(
            jwt,
            attemptId,
            InactivityCaptureOutcomes.Captured,
            includeFile: true);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("attempt_id").GetGuid().Should().Be(attemptId);
        body.GetProperty("evidence_asset_id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("file_record_id").GetGuid().Should().NotBeEmpty();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var attempt = await db.InactivityCaptureAttempts
            .SingleAsync(a => a.Id == attemptId && a.TenantId == user.TenantId);
        attempt.Outcome.Should().Be(InactivityCaptureOutcomes.Captured);
        attempt.EvidenceAssetId.Should().NotBeNull();

        var asset = await db.MonitoringEvidenceAssets
            .SingleAsync(a => a.Id == attempt.EvidenceAssetId);
        asset.TriggerType.Should().Be("inactivity_approved");
        asset.FileRecordId.Should().NotBeEmpty();
        asset.MetadataJson.Should().Contain("sha256");
    }

    [Fact]
    public async Task Submit_DeclinedWithoutFile_Returns200()
    {
        var slug = $"dec-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");
        var attemptId = Guid.NewGuid();

        using var req = BuildMultipartRequest(
            jwt,
            attemptId,
            InactivityCaptureOutcomes.Declined,
            includeFile: false);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var attempt = await db.InactivityCaptureAttempts
            .SingleAsync(a => a.Id == attemptId);
        attempt.Outcome.Should().Be(InactivityCaptureOutcomes.Declined);
        attempt.EvidenceAssetId.Should().BeNull();
    }

    [Fact]
    public async Task Submit_CapturedWithoutFile_Returns400()
    {
        var slug = $"bad-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await EnableCaptureTogglesAsync(user.TenantId);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = BuildMultipartRequest(
            jwt,
            Guid.NewGuid(),
            InactivityCaptureOutcomes.Captured,
            includeFile: false);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_Captured_WhenPolicyDisabled_Returns403()
    {
        var slug = $"pol-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");

        using var req = BuildMultipartRequest(
            jwt,
            Guid.NewGuid(),
            InactivityCaptureOutcomes.Captured,
            includeFile: true);

        var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_IdenticalRetry_IsIdempotent()
    {
        var slug = $"idem-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        await EnableCaptureTogglesAsync(user.TenantId);
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");
        var attemptId = Guid.NewGuid();

        using var first = BuildMultipartRequest(jwt, attemptId, InactivityCaptureOutcomes.Captured, includeFile: true);
        var firstResp = await _client.SendAsync(first);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK, await firstResp.Content.ReadAsStringAsync());
        var firstBody = await firstResp.Content.ReadFromJsonAsync<JsonElement>();

        using var second = BuildMultipartRequest(jwt, attemptId, InactivityCaptureOutcomes.Captured, includeFile: true);
        var secondResp = await _client.SendAsync(second);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK, await secondResp.Content.ReadAsStringAsync());
        var secondBody = await secondResp.Content.ReadFromJsonAsync<JsonElement>();

        secondBody.GetProperty("evidence_asset_id").GetGuid()
            .Should().Be(firstBody.GetProperty("evidence_asset_id").GetGuid());
        secondBody.GetProperty("file_record_id").GetGuid()
            .Should().Be(firstBody.GetProperty("file_record_id").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.InactivityCaptureAttempts.CountAsync(a => a.Id == attemptId)).Should().Be(1);
        (await db.MonitoringEvidenceAssets.CountAsync(a => a.TenantId == user.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Submit_ConflictingRetry_Returns409()
    {
        var slug = $"conf-{Guid.NewGuid():N}"[..20];
        var user = await SeedActiveUserAsync(slug, $"{slug}@test.dev", "TestPass1!");
        var jwt = await GetTrayJwtForUserAsync(user, $"fp-{slug}");
        var attemptId = Guid.NewGuid();

        using var declined = BuildMultipartRequest(jwt, attemptId, InactivityCaptureOutcomes.Declined, includeFile: false);
        (await _client.SendAsync(declined)).StatusCode.Should().Be(HttpStatusCode.OK);

        await EnableCaptureTogglesAsync(user.TenantId);
        using var captured = BuildMultipartRequest(jwt, attemptId, InactivityCaptureOutcomes.Captured, includeFile: true);
        var resp = await _client.SendAsync(captured);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("attempt_already_recorded");
    }

    [Fact]
    public async Task Submit_TenantARecord_IsNotVisibleToTenantB()
    {
        var slugA = $"ta-{Guid.NewGuid():N}"[..20];
        var slugB = $"tb-{Guid.NewGuid():N}"[..20];
        var userA = await SeedActiveUserAsync(slugA, $"{slugA}@test.dev", "TestPass1!");
        var userB = await SeedActiveUserAsync(slugB, $"{slugB}@test.dev", "TestPass1!");
        var jwtA = await GetTrayJwtForUserAsync(userA, $"fp-{slugA}");
        var attemptId = Guid.NewGuid();

        using var req = BuildMultipartRequest(jwtA, attemptId, InactivityCaptureOutcomes.Declined, includeFile: false);
        (await _client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await db.InactivityCaptureAttempts.CountAsync(a => a.TenantId == userA.TenantId)).Should().Be(1);
        (await db.InactivityCaptureAttempts.CountAsync(a => a.TenantId == userB.TenantId)).Should().Be(0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildMultipartRequest(
        string jwt,
        Guid attemptId,
        string outcome,
        bool includeFile)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(attemptId.ToString()), "attempt_id");
        content.Add(new StringContent("policy-int-1"), "policy_version");
        content.Add(new StringContent(IdleStart.ToString("O")), "idle_started_at");
        content.Add(new StringContent(PromptedAt.ToString("O")), "prompted_at");
        content.Add(new StringContent("300"), "idle_duration_seconds");
        content.Add(new StringContent(outcome), "outcome");

        if (outcome == InactivityCaptureOutcomes.Captured)
        {
            content.Add(new StringContent(DecisionAt.ToString("O")), "decision_at");
            content.Add(new StringContent(CapturedAt.ToString("O")), "captured_at");
            content.Add(new StringContent("2"), "monitor_count");
            content.Add(new StringContent("image/jpeg"), "content_type");
            content.Add(new StringContent("deadbeef"), "sha256");
            content.Add(new StringContent("-1920"), "virtual_bounds_x");
            content.Add(new StringContent("0"), "virtual_bounds_y");
            content.Add(new StringContent("3840"), "virtual_bounds_width");
            content.Add(new StringContent("1080"), "virtual_bounds_height");

            if (includeFile)
            {
                var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "file", "capture.jpg");
            }
        }
        else if (outcome == InactivityCaptureOutcomes.Declined)
        {
            content.Add(new StringContent(DecisionAt.ToString("O")), "decision_at");
            content.Add(new StringContent("0"), "monitor_count");
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/monitoring/tray/inactivity-attempts");
        req.Headers.Host = "localhost";
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = content;
        return req;
    }

    private async Task EnableCaptureTogglesAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MonitoringFeatureToggles.Add(new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActivityMonitoring = true,
            ScreenshotCapture = true,
            AutoScreenshotCapture = true,
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
