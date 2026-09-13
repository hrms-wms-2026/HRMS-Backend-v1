using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Integration tests for LegalEntityWorkModesController.
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

    public TenantSession Tenant { get; private set; } = null!;
    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }

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

        Tenant = await ProvisionAndLoginOwnerAsync("work-modes-test", "Work Modes Test Co", "owner@work-modes-test.test");
        TenantId = await GetTenantIdAsync(Tenant.Host);
        LegalEntityId = await GetPrimaryLegalEntityIdAsync(Tenant);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    public sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

    public async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
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
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());

        var completeResponse = await SendAsync(HttpMethod.Post, host, "/auth/v1/account/complete-owner-setup",
            new { email = ownerEmail, password = ownerPassword },
            idempotencyKey: Guid.NewGuid().ToString());
        completeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loginResponse = await SendAsync(HttpMethod.Post, host, "/auth/v1/login",
            new { email = ownerEmail, password = ownerPassword });
        var cookies = ParseSetCookies(loginResponse);
        var sessionCookie = $"session={cookies["session"]}";
        var csrfToken = cookies["csrf"];

        return new TenantSession(host, sessionCookie, csrfToken);
    }

    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == host.Split('.')[0]);
        return tenant?.Id ?? throw new InvalidOperationException($"Tenant not found for host {host}");
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(TenantSession tenant)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await GetTenantIdAsync(tenant.Host);
        var legalEntity = await context.LegalEntities.FirstOrDefaultAsync(le => le.TenantId == tenantId && le.IsPrimary);
        return legalEntity?.Id ?? throw new InvalidOperationException("Primary legal entity not found");
    }

    private async Task WaitForSeedersAsync()
    {
        var maxAttempts = 30;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var scope = _factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await context.Database.ExecuteSqlAsync($"SELECT 1");
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }
        throw new InvalidOperationException("Seeders failed to initialize");
    }

    private async Task GrantCoreHrStorageAllowanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == "work-modes-test");
        if (tenant != null)
        {
            tenant.ConcurrentUsersAllowed = 100;
            await context.SaveChangesAsync();
        }
    }

    private Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>();
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            foreach (var setCookieHeader in setCookieHeaders)
            {
                var parts = setCookieHeader.Split(';');
                if (parts.Length > 0)
                {
                    var keyValue = parts[0].Split('=');
                    if (keyValue.Length == 2)
                    {
                        cookies[keyValue[0].Trim()] = keyValue[1].Trim();
                    }
                }
            }
        }
        return cookies;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string host, string path, object? body = null,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, $"https://{host}{path}");
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }
        if (!string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.Add("X-CSRF-Token", csrfToken);
        }
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return await _client.SendAsync(request);
    }

    private async Task<object> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        try
        {
            return System.Text.Json.JsonDocument.Parse(content);
        }
        catch
        {
            return content;
        }
    }
}

public class LegalEntityWorkModesControllerTests : IClassFixture<LegalEntityWorkModesControllerTestsFixture>
{
    private readonly LegalEntityWorkModesControllerTestsFixture _fixture;

    public LegalEntityWorkModesControllerTests(LegalEntityWorkModesControllerTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task POST_CreateFiveWorkModes_AllSucceed()
    {
        var tenant = _fixture.Tenant;
        var legalEntityId = _fixture.LegalEntityId;

        for (int i = 1; i <= 5; i++)
        {
            var request = new
            {
                name = $"Mode {i}",
                biometricEnabled = i % 2 == 0,
                webEnabled = true,
                trayEnabled = i % 3 == 0,
                photoRequired = i > 3
            };

            var response = await SendAsync(HttpMethod.Post,
                $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
                request, tenant.SessionCookie, tenant.CsrfHeader);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task POST_CreateSixthWorkMode_Returns409Conflict()
    {
        var tenant = _fixture.Tenant;
        var legalEntityId = _fixture.LegalEntityId;

        // Create 5 work modes first
        for (int i = 1; i <= 5; i++)
        {
            var request = new
            {
                name = $"Mode {i}",
                biometricEnabled = false,
                webEnabled = true,
                trayEnabled = false,
                photoRequired = false
            };

            var response = await SendAsync(HttpMethod.Post,
                $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
                request, tenant.SessionCookie, tenant.CsrfHeader);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Try to create 6th
        var sixthRequest = new
        {
            name = "Mode 6",
            biometricEnabled = false,
            webEnabled = true,
            trayEnabled = false,
            photoRequired = false
        };

        var sixthResponse = await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            sixthRequest, tenant.SessionCookie, tenant.CsrfHeader);

        sixthResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PUT_UpdateWorkMode_SucceedsAndReturnsUpdatedData()
    {
        var tenant = _fixture.Tenant;
        var legalEntityId = _fixture.LegalEntityId;

        // Create a work mode
        var createRequest = new
        {
            name = "Original Name",
            biometricEnabled = false,
            webEnabled = true,
            trayEnabled = false,
            photoRequired = false
        };

        var createResponse = await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            createRequest, tenant.SessionCookie, tenant.CsrfHeader);

        var createdJson = await createResponse.Content.ReadAsAsync<dynamic>();
        var id = ((Newtonsoft.Json.Linq.JObject)createdJson)["id"].Value<string>();

        // Update it
        var updateRequest = new
        {
            name = "Updated Name",
            biometricEnabled = true,
            webEnabled = false,
            trayEnabled = true,
            photoRequired = true
        };

        var updateResponse = await SendAsync(HttpMethod.Put,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}",
            updateRequest, tenant.SessionCookie, tenant.CsrfHeader);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedJson = await updateResponse.Content.ReadAsAsync<dynamic>();
        ((Newtonsoft.Json.Linq.JObject)updatedJson)["name"].Value<string>().Should().Be("Updated Name");
        ((Newtonsoft.Json.Linq.JObject)updatedJson)["biometricEnabled"].Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task POST_DeactivateWorkMode_SetsIsActiveToFalse()
    {
        var tenant = _fixture.Tenant;
        var legalEntityId = _fixture.LegalEntityId;

        // Create a work mode
        var createRequest = new
        {
            name = "To Deactivate",
            biometricEnabled = false,
            webEnabled = true,
            trayEnabled = false,
            photoRequired = false
        };

        var createResponse = await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            createRequest, tenant.SessionCookie, tenant.CsrfHeader);

        var createdJson = await createResponse.Content.ReadAsAsync<dynamic>();
        var id = ((Newtonsoft.Json.Linq.JObject)createdJson)["id"].Value<string>();

        // Deactivate it
        var deactivateResponse = await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}/deactivate",
            null, tenant.SessionCookie, tenant.CsrfHeader);

        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GET_ListWorkModes_ExcludesInactiveByDefault()
    {
        var tenant = _fixture.Tenant;
        var legalEntityId = _fixture.LegalEntityId;

        // Create and deactivate one
        var createRequest = new
        {
            name = "Deactivated Mode",
            biometricEnabled = false,
            webEnabled = true,
            trayEnabled = false,
            photoRequired = false
        };

        var createResponse = await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            createRequest, tenant.SessionCookie, tenant.CsrfHeader);

        var createdJson = await createResponse.Content.ReadAsAsync<dynamic>();
        var id = ((Newtonsoft.Json.Linq.JObject)createdJson)["id"].Value<string>();

        await SendAsync(HttpMethod.Post,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes/{id}/deactivate",
            null, tenant.SessionCookie, tenant.CsrfHeader);

        // List without includeInactive
        var listResponse = await SendAsync(HttpMethod.Get,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes",
            null, tenant.SessionCookie, tenant.CsrfHeader);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsAsync<Newtonsoft.Json.Linq.JArray>();
        var deactivatedItem = listJson?.FirstOrDefault(x => x["id"]?.Value<string>() == id);
        deactivatedItem.Should().BeNull();

        // List with includeInactive=true
        var listWithInactiveResponse = await SendAsync(HttpMethod.Get,
            $"https://{tenant.Host}/api/v1/attendance/legal-entities/{legalEntityId}/work-modes?includeInactive=true",
            null, tenant.SessionCookie, tenant.CsrfHeader);

        listWithInactiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listWithInactiveJson = await listWithInactiveResponse.Content.ReadAsAsync<Newtonsoft.Json.Linq.JArray>();
        var deactivatedItemInclusive = listWithInactiveJson?.FirstOrDefault(x => x["id"]?.Value<string>() == id);
        deactivatedItemInclusive.Should().NotBeNull();
        deactivatedItemInclusive?["isActive"].Value<bool>().Should().BeFalse();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body,
        string sessionCookie, string csrfToken)
    {
        var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Add("Cookie", sessionCookie);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        return await new HttpClient().SendAsync(request);
    }
}
