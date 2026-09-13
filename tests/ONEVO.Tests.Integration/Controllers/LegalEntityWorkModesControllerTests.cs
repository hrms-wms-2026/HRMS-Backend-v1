using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Controllers;

/// <summary>
/// Fixture for LegalEntityWorkModesControllerTests.
/// </summary>
public sealed class LegalEntityWorkModesControllerTestsFixture : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public TenantSession TenantA { get; private set; } = null!;
    public Guid TenantAId { get; private set; }
    public Guid TenantAPrimaryLegalEntityId { get; private set; }

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        }
        else
        {
            await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        }
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();
        await GrantCoreHrStorageAllowanceAsync();

        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        TenantA = await ProvisionAndLoginOwnerAsync("workmode-test", "WorkMode Test Co", "owner-wm@test.test");
        TenantAId = await GetTenantIdAsync(TenantA.Host);
        TenantAPrimaryLegalEntityId = await GetPrimaryLegalEntityIdAsync(TenantA);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    public sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

    private async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
    {
        const string ownerPassword = "OwnerPass@2026!";
        var host = $"{slug}.localhost";

        var createBody = new
        {
            company_name = companyName,
            slug,
            industry_profile = "technology",
            company_size_range = "11-50",
            legal_entity_name = companyName,
            registration_number = $"PV-{slug}",
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
                email = ownerEmail,
                first_name = "Test",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createJson = await ReadJsonAsync(createResponse);
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendAsync(HttpMethod.Post, host,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = ownerPassword,
                confirm_password = ownerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmResponse = await SendAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email = ownerEmail, password = ownerPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginJson = await ReadJsonAsync(loginResponse);
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);

        return new TenantSession(host, sessionCookie, csrfHeader);
    }

    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await context.Tenants.SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(TenantSession session)
    {
        var list = await GetJsonAsync(session, "/api/v1/org/legal-entities");
        var primary = list.EnumerateArray().Single(i => i.GetProperty("isPrimary").GetBoolean());
        return primary.GetProperty("id").GetGuid();
    }

    // Each [Fact] gets its own Legal Entity so the 5-active-work-modes-per-legal-entity cap and
    // name-uniqueness checks can't interfere across tests sharing this IClassFixture.
    public async Task<Guid> CreateLegalEntityAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await SendAsync(
            HttpMethod.Post,
            TenantA.Host,
            "/api/v1/org/legal-entities",
            new
            {
                name = $"WorkMode Test LE {suffix}",
                companyCode = $"WM{suffix}",
                registrationNumber = $"REG-{suffix}",
                countryCode = "LKA",
                currencyCode = "LKR"
            },
            cookie: TenantA.SessionCookie,
            csrfToken: TenantA.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId != "tenant_owner_invite")
                    continue;
                if (!string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (template.Data.TryGetProperty("invite_token", out var token))
                    return token.GetString();
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task GrantCoreHrStorageAllowanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coreHr = await db.Set<ModuleCatalogItem>().SingleAsync(m => m.ModuleKey == "core_hr");
        coreHr.IsStorageConsuming = true;
        coreHr.StorageReference = """[{"min_employees":1,"max_employees":100,"storage_gb":50}]""";
        await db.SaveChangesAsync();
    }

    private async Task WaitForSeedersAsync()
    {
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
                var permissionsReady = await db.Set<Permission>().AnyAsync();
                var planReady = await db.Set<SubscriptionPlan>()
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

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string host, string path, object? body = null,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
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

    public async Task<JsonElement> GetJsonAsync(TenantSession session, string path)
    {
        var response = await SendAsync(HttpMethod.Get, session.Host, path, body: null, cookie: session.SessionCookie);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return json;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
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

[Collection(WebApplicationFactoryCollection.Name)]
public class LegalEntityWorkModesControllerTests : IClassFixture<LegalEntityWorkModesControllerTestsFixture>
{
    private readonly LegalEntityWorkModesControllerTestsFixture _fixture;

    public LegalEntityWorkModesControllerTests(LegalEntityWorkModesControllerTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task POST_Create_ValidRequest_Returns201()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        var response = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            new { name = "Onsite", biometricEnabled = true, webEnabled = true, trayEnabled = false, photoRequired = false },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_CreateMultiple_UpToFive_AllSucceed()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        for (int i = 1; i <= 5; i++)
        {
            var response = await _fixture.SendAsync(
                HttpMethod.Post,
                _fixture.TenantA.Host,
                $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
                new { name = $"Mode {i}", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
                cookie: _fixture.TenantA.SessionCookie,
                csrfToken: _fixture.TenantA.CsrfHeader);

            response.StatusCode.Should().Be(HttpStatusCode.Created, $"Mode {i} creation should succeed");
        }
    }

    [Fact]
    public async Task POST_CreateSixth_Returns409Conflict()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        // Create 5 first
        for (int i = 1; i <= 5; i++)
        {
            await _fixture.SendAsync(
                HttpMethod.Post,
                _fixture.TenantA.Host,
                $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
                new { name = $"Conflict Test {i}", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
                cookie: _fixture.TenantA.SessionCookie,
                csrfToken: _fixture.TenantA.CsrfHeader);
        }

        // Try 6th
        var response = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            new { name = "Conflict Test 6", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PUT_Update_ValidRequest_Returns200()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        // Create one first
        var createResponse = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            new { name = "Update Test", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        var json = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();

        // Update it
        var updateResponse = await _fixture.SendAsync(
            HttpMethod.Put,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}",
            new { name = "Updated Name", biometricEnabled = true, webEnabled = false, trayEnabled = true, photoRequired = true },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        updateJson.GetProperty("name").GetString().Should().Be("Updated Name");
    }

    [Fact]
    public async Task POST_Deactivate_ValidRequest_Returns204()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        // Create one first
        var createResponse = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            new { name = "Deactivate Test", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        var json = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();

        // Deactivate it
        var deactivateResponse = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}/deactivate",
            body: null,
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GET_List_ExcludesInactiveByDefault()
    {
        var legalEntityId = await _fixture.CreateLegalEntityAsync();

        // Create and deactivate one
        var createResponse = await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            new { name = "List Inactive Test", biometricEnabled = false, webEnabled = true, trayEnabled = false, photoRequired = false },
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        var json = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();

        await _fixture.SendAsync(
            HttpMethod.Post,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}/deactivate",
            body: null,
            cookie: _fixture.TenantA.SessionCookie,
            csrfToken: _fixture.TenantA.CsrfHeader);

        // List without includeInactive
        var listResponse = await _fixture.SendAsync(
            HttpMethod.Get,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            body: null,
            cookie: _fixture.TenantA.SessionCookie);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var deactivatedItem = listJson.EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetGuid() == id);
        deactivatedItem.Should().Be(default(JsonElement), "deactivated items should not appear in default list");

        // List with includeInactive=true
        var listWithInactiveResponse = await _fixture.SendAsync(
            HttpMethod.Get,
            _fixture.TenantA.Host,
            $"/api/v1/attendance/legal-entities/{legalEntityId}/work-modes?includeInactive=true",
            body: null,
            cookie: _fixture.TenantA.SessionCookie);

        listWithInactiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listWithInactiveJson = await listWithInactiveResponse.Content.ReadFromJsonAsync<JsonElement>();
        var deactivatedItemInclusive = listWithInactiveJson.EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetGuid() == id);
        deactivatedItemInclusive.Should().NotBe(default(JsonElement), "deactivated items should appear when includeInactive=true");
        deactivatedItemInclusive.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }
}
